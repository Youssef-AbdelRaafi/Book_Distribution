#!/usr/bin/env python3
"""Reconcile the 2025-2026 manual DOCX ledgers with the website SQLite data.

The former import used a text extraction that could carry a library name from a
later template page into an earlier ledger.  This utility instead reads every
table in document order, keeps the closest explicit addressee/account heading,
and writes a reviewable manifest before it changes the database.

Default mode is read-only.  Use --apply only after reviewing --report.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sqlite3
import sys
from collections import Counter
from dataclasses import asdict, dataclass
from datetime import date
from pathlib import Path
from typing import Iterable

from docx import Document
from docx.oxml.ns import qn
from docx.table import Table
from docx.text.paragraph import Paragraph


ROOT = Path(__file__).resolve().parents[2]
SOURCE_DIR = ROOT / "2025-2026"
DEFAULT_DB = ROOT / "BookDistributionAPI" / "new_database.db"


def compact(value: str) -> str:
    """Normalize Arabic variants and non-semantic formatting for matching."""
    value = value or ""
    value = value.replace("أ", "ا").replace("إ", "ا").replace("آ", "ا")
    value = value.replace("ى", "ي").replace("ة", "ه")
    value = re.sub(r"[\u064b-\u065f\u0670]", "", value)
    value = re.sub(r"[^\w\s]", " ", value, flags=re.UNICODE)
    return " ".join(value.lower().split())


def clean(value: str) -> str:
    return " ".join((value or "").replace("\xa0", " ").split())


def unique_cells(row) -> list[str]:
    cells: list[str] = []
    for cell in row.cells:
        value = clean(cell.text)
        if not cells or value != cells[-1]:
            cells.append(value)
    return cells


def parse_number(value: str) -> float | None:
    value = clean(value).replace(",", "")
    value = value.replace("٫", ".")
    match = re.search(r"-?\d+(?:\.\d+)?", value)
    return float(match.group(0)) if match else None


def number_or_zero(value: str) -> int:
    parsed = parse_number(value)
    return int(parsed) if parsed is not None else 0


def table_text(table: Table) -> str:
    return " ".join(clean(cell.text) for row in table.rows for cell in row.cells)


def classify_table(table: Table) -> str | None:
    text = compact(table_text(table))
    if "اجمالي ما تم ارساله" in text and "عدد المرتجع" in text:
        return "clearance"
    if "التفاصيل" in text and "الكميه" in text and ("السعر" in text or "rate" in text):
        return "invoice"
    return None


def normalize_book_name(value: str, term: str) -> str | None:
    value = compact(value)
    if "فيزياء" in value and "تاسع" in value:
        return "physics_9"
    if "كيمياء" in value and "تاسع" in value:
        return "chemistry_9"
    if "فيزياء" in value and "عاشر" in value:
        return "physics_10"
    if "كيمياء" in value and "عاشر" in value:
        return "chemistry_10"
    if "فيزياء" in value and ("الحادي عشر" in value or "11" in value):
        return "physics_11"
    if "علوم" in value and "بيئي" in value:
        if "11" in value or "الحادي عشر" in value or "ادبي" in value and term == "A":
            return "environment_11"
        return "environment_12"
    if "فيزياء" in value and ("الثاني عشر" in value or "12" in value):
        return "physics_12"
    return None


@dataclass
class Item:
    book_key: str
    quantity: int
    price: float | None
    source_name: str
    sent_quantity: int | None = None


@dataclass
class ParsedTable:
    source: str
    kind: str
    term: str | None
    library_heading: str | None
    date_text: str | None
    items: list[Item]


def parse_invoice_table(table: Table, term: str | None) -> list[Item]:
    items: list[Item] = []
    for row in table.rows[1:]:
        cells = unique_cells(row)
        if len(cells) < 4:
            continue
        joined = " | ".join(cells)
        book_key = normalize_book_name(joined, term or "A")
        if not book_key:
            continue
        # Invoice tables consistently end in description, quantity, rate, amount.
        numeric = [parse_number(cell) for cell in cells]
        numeric = [n for n in numeric if n is not None]
        if len(numeric) < 3:
            continue
        quantity = int(numeric[-3])
        price = float(numeric[-2])
        if quantity <= 0:
            continue
        source_name = next((cell for cell in cells if normalize_book_name(cell, term or "A")), joined)
        items.append(Item(book_key, quantity, price if price > 0 else None, source_name))
    return items


def parse_clearance_table(table: Table, term: str | None) -> list[Item]:
    items: list[Item] = []
    for row in table.rows[1:]:
        cells = unique_cells(row)
        if len(cells) < 3:
            continue
        joined = " | ".join(cells)
        book_key = normalize_book_name(joined, term or "A")
        if not book_key:
            continue
        # The first numeric column after the book title is sent, then returned.
        first_book_cell = next(
            (index for index, cell in enumerate(cells) if normalize_book_name(cell, term or "A")),
            0,
        )
        numeric = [number_or_zero(cell) for cell in cells[first_book_cell + 1 :]]
        if not numeric:
            continue
        sent = numeric[0]
        returned = numeric[1] if len(numeric) > 1 else 0
        if sent > 0:
            items.append(
                Item(
                    book_key,
                    returned,
                    None,
                    next((c for c in cells if normalize_book_name(c, term or "A")), joined),
                    sent_quantity=sent,
                )
            )
    return items


def extract_date(value: str) -> str | None:
    """Return ISO date when a manual heading includes a real date."""
    value = value or ""
    match = re.search(r"(\d{1,2})\s*/\s*(\d{1,2})\s*/\s*(\d{2,4})", value)
    if not match:
        return None
    day, month, year = map(int, match.groups())
    if year < 100:
        year += 2000
    try:
        return date(year, month, day).isoformat()
    except ValueError:
        return None


def heading_from_paragraph(value: str) -> str | None:
    value = clean(value)
    if "الفاضل/" not in value and "حساب مكتبة" not in value and "حساب متجر" not in value:
        return None
    if "الفاضل/" in value:
        value = value.split("الفاضل/", 1)[1]
    value = re.split(r"التاريخ|بتاريخ", value, maxsplit=1)[0]
    value = value.replace("الفاضل/", "").strip(" -–—")
    return clean(value) or None


def term_from_paragraph(value: str) -> str | None:
    normalized = compact(value)
    if "الفصل الدراسي الاول" in normalized:
        return "A"
    if "الفصل الدراسي الثاني" in normalized:
        return "B"
    return None


def parse_docx(path: Path) -> list[ParsedTable]:
    document = Document(path)
    term: str | None = None
    heading: str | None = None
    date_text: str | None = None
    result: list[ParsedTable] = []
    for child in document.element.body.iterchildren():
        if child.tag == qn("w:p"):
            paragraph = clean(Paragraph(child, document).text)
            detected_term = term_from_paragraph(paragraph)
            if detected_term:
                term = detected_term
            detected_heading = heading_from_paragraph(paragraph)
            if detected_heading:
                heading = detected_heading
                date_text = extract_date(paragraph)
            elif "بتاريخ" in paragraph:
                date_text = extract_date(paragraph) or date_text
            continue
        if child.tag != qn("w:tbl"):
            continue
        table = Table(child, document)
        kind = classify_table(table)
        if kind == "invoice":
            items = parse_invoice_table(table, term)
        elif kind == "clearance":
            items = parse_clearance_table(table, term)
        else:
            continue
        if items:
            result.append(
                ParsedTable(
                    source=str(path.relative_to(ROOT)).replace("\\", "/"),
                    kind=kind,
                    term=term,
                    library_heading=heading,
                    date_text=date_text,
                    items=items,
                )
            )
    return result


def inferred_term(table: ParsedTable) -> str | None:
    """Infer a term for ledger templates that omit the printed term heading."""
    if table.term:
        return table.term
    if table.date_text:
        year = int(table.date_text[:4])
        if year == 2025:
            return "A"
        if year == 2026:
            return "B"
    if table.kind == "invoice":
        # The source prices distinguish the two editions even in invoice forms
        # where the "first/second term" title was left blank.
        for item in table.items:
            source_name = compact(item.source_name)
            if "العلوم البيئيه ادبي" in source_name:
                return "B"
            if item.book_key == "physics_11" and item.price == 4:
                return "B"
            if item.book_key == "environment_12":
                return "B"
            if item.book_key == "physics_11" and item.price == 3.5:
                return "A"
            if item.book_key == "environment_11" and item.price == 4:
                return "A"
            if item.book_key == "chemistry_10":
                return "A"
    else:
        keys = {item.book_key for item in table.items}
        if "environment_12" in keys:
            return "B"
        if "chemistry_10" in keys:
            return "A"
    return None


def infer_missing_terms(tables: list[ParsedTable]) -> list[ParsedTable]:
    """Fill only unambiguous missing terms, first from source evidence then context."""
    by_source: dict[str, list[ParsedTable]] = {}
    for table in tables:
        table.term = inferred_term(table)
        by_source.setdefault(table.source, []).append(table)
    for group in by_source.values():
        known = [index for index, table in enumerate(group) if table.term]
        for index, table in enumerate(group):
            if table.term or not known:
                continue
            closest = min(known, key=lambda known_index: abs(known_index - index))
            table.term = group[closest].term
    # These two records retain only a second-term clearance page.  The source
    # totals and the independent historic invoice table confirm the term.
    for table in tables:
        source_key = compact(table.source)
        if not table.term and ("مكتبه الوطن السويق" in source_key or "مكتبه شروق العلم" in source_key):
            table.term = "B"
    return tables


# Exact source identities that are not simple spelling variants of a website row.
NEW_LIBRARY_SPECS = {
    "مكتبات نزوي": (5, 23),
    "مكتبة نوح": (10, 54),
    "مكتبة الآفاق بالخابورة": (6, 35),
    "مكتبة الفاروق": (6, 36),
    "مكتبة الألوان بشناص": (6, 32),
    "مكتبة شروق العلم": (6, 32),
    "مكتبة دار الشروق + مكتبة منار السبيل + مكتبة اللبيب": (6, 34),
    "مكتبة الهداية فرع القوف": (2, 7),
    "مكتبة المنارة بالحيل الجنوبية": (1, 3),
    "مكتبة الجامعة - فرع المعبيلة + بوشر": (1, 3),
    "مكتبة الإدريسي - العذيبة - الأنصب - بوشر - الوادي الكبير": (1, 2),
}


def heading_alias(heading: str) -> str:
    normalized = compact(heading)
    normalized = re.sub(r"^حساب\s+(?:مكتبه|متجر)\s*", "", normalized)
    aliases: list[tuple[str, str]] = [
        ("متجر السعاده", "متجر السعادة"),
        ("السعاده", "متجر السعادة"),
        ("مكتبه السندباد", "مكتبة السندباد"),
        ("السندباد", "مكتبة السندباد"),
        ("السيده فاطمه الزهراء", "مكتبة السيدة فاطمة الزهراء أدم"),
        ("الفضل ابن الحواري", "مكتبة الفضل بن الحواري"),
        ("الفضل بن الحواري", "مكتبة الفضل بن الحواري"),
        ("اجيال ازكي", "مكتبة أجيال ازكي"),
        ("الحمراء الحديثه", "مكتبة الحمراء الحديثة"),
        ("الحمرا الحديثه", "مكتبة الحمراء الحديثة"),
        ("الاثراء", "مكتبة الإثراء"),
        ("الغبيراء", "مكتبة الغبيراء"),
        ("بن الهاشمي", "مكتبة ابن الهاشمي"),
        ("صدي القمه", "مكتبة صدى القمة"),
        ("واحه التفوق", "مكتبة واحة التفوق"),
        ("بيت الجبل", "مكتبة بيت الجبل"),
        ("غايه التميز", "مكتبة غاية التميز"),
        ("مكتبات نزوي", "مكتبات نزوي"),
        ("اقرا", "مكتبة اقرأ"),
        ("الابتكار", "مكتبة الابتكار"),
        ("الشهامه", "مكتبة الشهامة"),
        ("دبوس", "مكتبة دبوس"),
        ("الجامعه فرع السيب", "مكتبة الجامعة (المعبيلة)"),
        ("مكتبه نوح", "مكتبة نوح"),
        ("نوح", "مكتبة نوح"),
        ("كنوز المعرفه", "مكتبة كنوز المعرفة"),
        ("واحه الظاهره", "مكتبة واحة الظاهر"),
        ("دار النور", "مكتبة دار النور"),
        ("طبشوره", "مكتبة طبشورة"),
        ("الامنيات الكبيره", "مكتبة الأمنيات الكبيرة"),
        ("الراقي السوادي العالميه", "مكتبة الراقي العالمية بالسوادي"),
        ("الراقي العالميه بالسوادي", "مكتبة الراقي العالمية بالسوادي"),
        ("الراقي بالمصنعه", "مكتبة الراقي"),
        ("الطيف", "مكتبة الطيف"),
        ("الفلاح", "مكتبة الفلاح"),
        ("المعراج", "مكتبة المعراج"),
        ("الكوفه", "مكتبة الكوفة"),
        ("الوافي", "مكتبة الوافي"),
        ("سما الابداع", "مكتبة سما الإبداع"),
        ("نور الاستقامه", "مكتبة نور الاستقامة"),
        ("اطلس", "مكتبة أطلس"),
        ("الكاس", "مكتبة الكأس"),
        ("طيور الجنه", "مكتبة طيور الجنة"),
        ("الافاق", "مكتبة الآفاق بالخابورة"),
        ("دار العلم", "مكتبة دار العلم"),
        ("شعاع القلم", "مكتبة شعاع القلم"),
        ("الاتحاد بجوار الوان", "مكتبة الاتحاد بجوار ألوان"),
        ("الفاروق", "مكتبة الفاروق"),
        ("الفجر الجديد 1", "مكتبة الفجر الجديد بالثرمد"),
        ("الفجر الجديد 2", "مكتبة الفجر الجديد فرع الإسكان"),
        ("الفجر الجديد بالثرمد", "مكتبة الفجر الجديد بالثرمد"),
        ("الفجر الجديد فرع الاسكان", "مكتبة الفجر الجديد فرع الإسكان"),
        ("الفجر الجديد بالبطحاء", "مكتبة الفجر الجديد فرع الإسكان"),
        ("المتنبي", "مكتبة المتنبي"),
        ("الوطن", "مكتبة الوطن"),
        ("اليسر", "مكتبة اليُسر (الخضراء سابقاً)"),
        ("زهي السلام", "مكتبة زهي السلام"),
        ("الصدف", "مكتبة الصدف"),
        ("الوان بشناص", "مكتبة الألوان بشناص"),
        ("شروق العلم", "مكتبة شروق العلم"),
        ("الشرق الاوسط", "مكتبة الشرق الأوسط"),
        ("المدينه", "مكتبة المدينة"),
        ("برج القاهره", "مكتبة برج القاهرة"),
        ("روائع الامل", "مكتبة روائع الأمل"),
        ("روائع البيان", "مكتبة روائع البيان"),
        ("عهود الهطالي", "متجر العهود صحم"),
        ("دار الشروق", "مكتبة دار الشروق + مكتبة منار السبيل + مكتبة اللبيب"),
        ("الشروق واللبيب", "مكتبة دار الشروق + مكتبة منار السبيل + مكتبة اللبيب"),
        ("الكندي", "مكتبة الكندي"),
        ("مناهل العلم", "مكتبة مناهل العلم"),
        ("خلفان", "مكتبة خلفان"),
        ("دار العروبه", "مكتبة دار العروبة"),
        ("المجره المضيئه", "مكتبة المجرة المضيئة"),
        ("كنوز العلم", "مكتبة كنوز العلم"),
        ("روائع نور الاستقامه", "مكتبة روائع نور الاستقامة"),
        ("قرطاسيه انهار", "مكتبة قرطاسية انهار سناو"),
        ("انهار سناو", "مكتبة قرطاسية انهار سناو"),
        ("مكتبه a4", "مكتبة A4 بصلحنوت"),
        ("الخريف فرع السعاده", "مكتبة الخريف فرع السعادة"),
        ("الخريف فرع شارع السلام", "مكتبة الخريف فرع شارع السلام"),
        ("الخريف شارع السلام", "مكتبة الخريف فرع شارع السلام"),
        ("الخريف فرع صلاله", "مكتبة الخريف فرع صلالة الجديدة"),
        ("الثقافه الاسلاميه", "مكتبة الثقافة الاسلامية"),
        ("الهدايه فرع القوف", "مكتبة الهداية فرع القوف"),
        ("الهدايه فرع شارع السلام", "مكتبة الهداية فرع شارع السلام"),
        ("الهدايه فرع صلاله الجديده", "مكتبة الهداية فرع صلالة الجديدة"),
        ("اشرف رشاد", "متجر أشرف رشاد"),
        ("مزون", "مكتبة مزون"),
        ("مكتبتك", "مكتبة (مكتبتك)"),
        ("الارتقاء", "مكتبة الارتقاء"),
        ("دار المناهل", "مكتبة دار المناهل"),
        ("ورقه وقلم", "مكتبة ورقة وقلم"),
        ("الادريسي", "مكتبة الإدريسي - العذيبة - الأنصب - بوشر - الوادي الكبير"),
        ("الجامعه مسقط فرع المعبيله بوشر", "مكتبة الجامعة - فرع المعبيلة + بوشر"),
        ("الجامعه فرع المعبيله وبوشر", "مكتبة الجامعة - فرع المعبيلة + بوشر"),
        ("القلم الاخضر", "مكتبة القلم الأخضر"),
        ("الجامعه للقراء", "مكتبة الجامعة للقراء"),
        ("الوردي", "مكتبة الوردي"),
        ("الهدايه فرع مسقط", "مكتبة الهداية"),
        ("الهدايه بالوادي الكبير", "مكتبة الهداية"),
        ("الهدايه سابقا فرع الوادي الكبير", "مكتبة الهداية"),
        ("السطر", "مكتبة السطر"),
        ("زهره المدائن", "مكتبة زهرة المدائن"),
        ("المجد", "مكتبة المجد"),
        ("قريات الثقافيه", "مكتبة قريات الثقافية"),
        ("قريات مسقط", "مكتبة قريات الثقافية"),
        ("واحه الظاهر", "مكتبة واحة الظاهر"),
        ("المناره بالحيل", "مكتبة المنارة بالحيل الجنوبية"),
        ("مكتبه مسقط", "مكتبة مسقط"),
        ("مسقط بالحيل", "مكتبة مسقط"),
        ("مسقط الحيل", "مكتبة مسقط"),
    ]
    for token, target in aliases:
        if token in normalized:
            return target
    return heading


def source_overrides(source: str) -> str | None:
    source = compact(source)
    if "عهود الهطالي" in source:
        return "متجر العهود صحم"
    if "مناهل العلم" in source:
        return "مكتبة مناهل العلم"
    if "اشرف رشاد" in source:
        return "متجر أشرف رشاد"
    if "الشرق الاوسط" in source:
        return "مكتبة الشرق الأوسط"
    return None


def table_signature(table: ParsedTable) -> tuple:
    return (
        table.kind,
        table.term,
        heading_alias(table.library_heading or ""),
        tuple((item.book_key, item.quantity, item.price) for item in table.items),
    )


def deduplicate_carbon_copies(tables: Iterable[ParsedTable]) -> list[ParsedTable]:
    result: list[ParsedTable] = []
    prior_by_source: dict[str, tuple] = {}
    for table in tables:
        signature = table_signature(table)
        # A carbon copy immediately repeats the same invoice table.  Do not
        # collapse repeats separated by another financial table: those may be
        # independently issued invoices with equal quantities.
        if table.kind == "invoice" and prior_by_source.get(table.source) == signature:
            continue
        prior_by_source[table.source] = signature
        result.append(table)
    return result


# Five source files include an exact duplicate of a ledger already retained in
# the library's own folder.  These are supporting/copy pages, not a second
# delivery.  The key deliberately names only the five independently verified
# cases; it never removes coincidentally similar invoices in other records.
CROSS_FILE_COPY_PREFERENCE = {
    ("مكتبة اليُسر (الخضراء سابقاً)", "A", "clearance"): "مكتبة اليسر الخضراء سابقا",
    ("مكتبة الخريف فرع صلالة الجديدة", "A", "invoice"): "مكتبات الخريف/الخريف فرع صلالة.docx",
    ("مكتبة مزون", "B", "invoice"): "مسقط/الخوض/مكتبة مزون.docx",
    ("مكتبة مسقط", "B", "invoice"): "مسقط الحيل الجنوبية/مكتبة مسق بمسقط.docx",
    ("مكتبة الارتقاء", "B", "invoice"): "مسقط/مكتبة الارتقاء.docx",
}


def remove_verified_cross_file_copies(entries: list[dict]) -> tuple[list[dict], int]:
    grouped: dict[tuple, list[int]] = {}
    for index, entry in enumerate(entries):
        if entry["kind"] == "implied_order":
            continue
        signature = (
            entry["library_target"],
            entry["term"],
            entry["kind"],
            entry["date_text"],
            tuple((item["book_key"], item["quantity"], item["price"]) for item in entry["items"]),
        )
        grouped.setdefault(signature, []).append(index)
    discard: set[int] = set()
    for signature, indexes in grouped.items():
        preference = CROSS_FILE_COPY_PREFERENCE.get(signature[:3])
        if not preference or len(indexes) < 2:
            continue
        canonical = [index for index in indexes if preference in entries[index]["source"]]
        if len(canonical) != 1:
            raise ValueError(f"Unexpected source-copy match for {signature[:3]}")
        discard.update(index for index in indexes if index != canonical[0])
    return [entry for index, entry in enumerate(entries) if index not in discard], len(discard)


BOOK_IDS = {
    "A": {
        "physics_9": 19,
        "chemistry_9": 20,
        "physics_10": 21,
        "chemistry_10": 22,
        "physics_11": 23,
        "environment_11": 24,
        "physics_12": 25,
    },
    "B": {
        "physics_9": 28,
        "chemistry_9": 29,
        "physics_10": 30,
        "physics_11": 31,
        "environment_11": 32,
        "physics_12": 33,
        "environment_12": 34,
    },
}


def fallback_date(term: str, kind: str) -> str:
    if kind == "implied_order":
        kind = "invoice"
    return {
        ("A", "invoice"): "2025-09-01",
        ("A", "clearance"): "2025-12-15",
        ("B", "invoice"): "2026-02-01",
        ("B", "clearance"): "2026-05-15",
    }[(term, kind)]


def build_manifest() -> dict:
    parsed: list[ParsedTable] = []
    excluded_relative_paths = {
        "2025-2026/متجر السعادة.docx",
        "2025-2026/نموذج ترم أول.docx",
        "2025-2026/نموذج ترم ثاني.docx",
        "2025-2026/نموذج جرد + سند قبض.docx",
        "2025-2026/نموذج سند قبض.docx",
    }
    source_files = sorted(
        path for path in SOURCE_DIR.rglob("*.docx")
        if not path.name.startswith("~$")
        and str(path.relative_to(ROOT)).replace("\\", "/") not in excluded_relative_paths
    )
    for path in source_files:
        parsed.extend(parse_docx(path))
    parsed = infer_missing_terms(deduplicate_carbon_copies(parsed))
    entries = []
    unresolved = []
    for table in parsed:
        target = source_overrides(table.source) or heading_alias(table.library_heading or "")
        entry = asdict(table)
        entry["library_target"] = target
        if not table.term or (not table.library_heading and not source_overrides(table.source)):
            unresolved.append({"source": table.source, "reason": "missing term or library heading", "table": entry})
        entries.append(entry)

    entries, removed_cross_file_copies = remove_verified_cross_file_copies(entries)

    # A settlement table proves that a consignment was sent.  It normally has
    # its matching delivery invoice in the same DOCX.  If that invoice is not
    # present (several ledgers retain only the settlement page), create one
    # implied source order from the documented sent quantities.
    invoice_keys = {
        (entry["source"], entry["library_target"], entry["term"])
        for entry in entries
        if entry["kind"] == "invoice"
    }
    implied_orders = []
    for entry in entries:
        source_key = (entry["source"], entry["library_target"], entry["term"])
        if entry["kind"] != "clearance" or source_key in invoice_keys or not entry["term"]:
            continue
        sent_items = [
            {
                **item,
                "quantity": item["sent_quantity"] or 0,
                "price": None,
            }
            for item in entry["items"]
            if (item["sent_quantity"] or 0) > 0
        ]
        if sent_items:
            implied_orders.append({
                **entry,
                "kind": "implied_order",
                "items": sent_items,
            })
    entries.extend(implied_orders)
    return {
        "source_docx_count": len(source_files),
        "financial_table_count": len(entries),
        "kind_counts": dict(Counter(entry["kind"] for entry in entries)),
        "term_counts": dict(Counter(entry["term"] for entry in entries)),
        "removed_verified_cross_file_copies": removed_cross_file_copies,
        "unresolved": unresolved,
        "tables": entries,
    }


def database_libraries(connection: sqlite3.Connection) -> dict[str, sqlite3.Row]:
    return {compact(row["Name"]): row for row in connection.execute("SELECT * FROM Libraries")}


def ensure_library(connection: sqlite3.Connection, target: str) -> sqlite3.Row:
    libraries = database_libraries(connection)
    existing = libraries.get(compact(target))
    if existing:
        return existing
    spec = NEW_LIBRARY_SPECS.get(target)
    if not spec:
        raise ValueError(f"No location specification for source library: {target}")
    governorate_id, city_id = spec
    connection.execute(
        """
        INSERT INTO Libraries (Name, GovernorateId, CityId, OwnerName, OwnerPhone,
                               ResponsibleName, ResponsiblePhone, Shift1Start,
                               Shift1End, IsActive)
        VALUES (?, ?, ?, '', '', '', '', '08:00', '22:00', 1)
        """,
        (target, governorate_id, city_id),
    )
    return database_libraries(connection)[compact(target)]


def apply_manifest(db_path: Path, manifest: dict) -> None:
    if manifest["unresolved"]:
        raise ValueError("Refusing to apply: manifest contains unresolved source tables")
    connection = sqlite3.connect(db_path)
    connection.row_factory = sqlite3.Row
    try:
        connection.execute("PRAGMA foreign_keys = ON")
        with connection:
            # Preserve all user-entered 2026 invoices while replacing only the
            # previous academic-2025 import.
            connection.execute(
                "DELETE FROM InvoiceItems WHERE InvoiceId IN (SELECT Id FROM Invoices WHERE InvoiceYear = 2025)"
            )
            connection.execute("DELETE FROM Invoices WHERE InvoiceYear = 2025")
            connection.execute("UPDATE Semesters SET IsActive = 1 WHERE Id IN (3, 4)")
            # The source folder explicitly labels Ajeel Izki as cancelled,
            # whereas Al-Sindbad has an active 2025-2026 ledger.
            connection.execute("UPDATE Libraries SET IsActive = 0 WHERE Name = 'مكتبة أجيال ازكي'")
            connection.execute("UPDATE Libraries SET IsActive = 1 WHERE Name = 'مكتبة السندباد'")

            source_prices: dict[tuple[str, str, str, str], float] = {}
            for entry in manifest["tables"]:
                if entry["kind"] != "invoice":
                    continue
                for item in entry["items"]:
                    if item["price"] is not None:
                        source_prices[(entry["source"], entry["library_target"], entry["term"], item["book_key"])] = item["price"]

            counters: Counter[tuple[int, int]] = Counter()
            for entry in manifest["tables"]:
                if entry["kind"] == "clearance" and not entry["items"]:
                    continue
                library = ensure_library(connection, entry["library_target"])
                term = entry["term"]
                semester_id = 3 if term == "A" else 4
                invoice_type = "refund" if entry["kind"] == "clearance" else "order"
                items = entry["items"]
                valid = [item for item in items if item["quantity"] > 0 and item["book_key"] in BOOK_IDS[term]]
                if not valid:
                    continue
                counters[(library["Id"], semester_id)] += 1
                invoice_date = entry["date_text"] or fallback_date(term, entry["kind"])
                connection.execute(
                    """
                    INSERT INTO Invoices (InvoiceNumber, InvoiceYear, TermCode, Type,
                        LibraryId, SemesterId, Date, TotalAmount, PrintStatus,
                        ResponsibleName, ResponsiblePhone, LibraryName, IsActive)
                    VALUES (?, 2025, ?, ?, ?, ?, ?, 0, 'printed', ?, ?, ?, 1)
                    """,
                    (
                        counters[(library["Id"], semester_id)], term, invoice_type,
                        library["Id"], semester_id, invoice_date,
                        library["ResponsibleName"], library["ResponsiblePhone"], library["Name"],
                    ),
                )
                invoice_id = connection.execute("SELECT last_insert_rowid()").fetchone()[0]
                total = 0.0
                for item in valid:
                    book_id = BOOK_IDS[term][item["book_key"]]
                    book = connection.execute("SELECT Name, Grade, Price FROM Books WHERE Id = ?", (book_id,)).fetchone()
                    price = item["price"]
                    if price is None:
                        price = source_prices.get(
                            (entry["source"], entry["library_target"], term, item["book_key"]),
                            float(book["Price"]),
                        )
                    line_total = round(item["quantity"] * price, 3)
                    total += line_total
                    connection.execute(
                        """
                        INSERT INTO InvoiceItems (InvoiceId, BookId, BookName, BookGrade,
                            Quantity, UnitPrice, Total)
                        VALUES (?, ?, ?, ?, ?, ?, ?)
                        """,
                        (invoice_id, book_id, book["Name"], book["Grade"], item["quantity"], price, line_total),
                    )
                connection.execute("UPDATE Invoices SET TotalAmount = ? WHERE Id = ?", (round(total, 3), invoice_id))
    finally:
        connection.close()


def write_report(path: Path, manifest: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", type=Path, default=DEFAULT_DB)
    parser.add_argument("--report", type=Path, default=ROOT / "tmp" / "reconciliation_2025_2026_manifest.json")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    manifest = build_manifest()
    write_report(args.report, manifest)
    print(json.dumps({key: manifest[key] for key in ("source_docx_count", "financial_table_count", "kind_counts", "term_counts")}, ensure_ascii=False))
    print(f"Unresolved tables: {len(manifest['unresolved'])}")
    print(f"Report: {args.report}")
    if args.apply:
        apply_manifest(args.db, manifest)
        print(f"Applied to: {args.db}")
    return 0 if not manifest["unresolved"] else 2


if __name__ == "__main__":
    sys.exit(main())
