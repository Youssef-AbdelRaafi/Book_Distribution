"""
Complete 2025-2026 Database Migration Script
Cleans broken data and inserts correct invoices from manual documents.
"""
import sqlite3, json, shutil, datetime, os, sys, io, re, difflib

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

DB_PATH = r"f:\BookDistribution_Project\BookDistributionAPI\new_database.db"
JSON_PATH = r"C:\Users\super magic\.gemini\antigravity\brain\85e883c5-64d3-41c8-bf1d-cf0833ca002e\scratch\extracted_data.json"

# ── Phase 1: Backup ────────────────────────────────────────────────────
ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
BACKUP = DB_PATH + f".backup_{ts}"
shutil.copy2(DB_PATH, BACKUP)
print(f"✅ Phase 1: Backed up to {BACKUP}")

# ── Load extracted data ────────────────────────────────────────────────
with open(JSON_PATH, 'r', encoding='utf-8') as f:
    data = json.load(f)

conn = sqlite3.connect(DB_PATH)
conn.row_factory = sqlite3.Row
cur = conn.cursor()

# ── Phase 2: Build Library Name Mapping ────────────────────────────────
cur.execute("SELECT Id, Name, GovernorateId, CityId, ResponsibleName, ResponsiblePhone FROM Libraries")
db_libs = {row['Id']: dict(row) for row in cur.fetchall()}

# Book short name → Book ID mapping
TERM_A_BOOK_MAP = {
    'فيزياء التاسع': 19, 'كيمياء التاسع': 20,
    'فيزياء العاشر': 21, 'كيمياء العاشر': 22,
    'فيزياء الحادي عشر': 23, 'العلوم البيئية 11': 24,
    'العلوم البيئية': 24, 'فيزياء الثاني عشر': 25,
}
TERM_B_BOOK_MAP = {
    'فيزياء التاسع': 28, 'كيمياء التاسع': 29,
    'فيزياء العاشر': 30, 'فيزياء الحادي عشر': 31,
    'العلوم البيئية 12': 34, 'العلوم البيئية': 34,
    'فيزياء الثاني عشر': 33,
}

# Load book info from DB
cur.execute("SELECT Id, Name, Grade, Price, SemesterId FROM Books WHERE SemesterId IN (3,4) AND IsActive=1")
book_info = {row['Id']: dict(row) for row in cur.fetchall()}

def norm(s):
    if not s: return ""
    s = s.replace('أ','ا').replace('إ','ا').replace('آ','ا').replace('ة','ه').replace('ى','ي')
    s = re.sub(r'\s+', ' ', s).strip()
    return s

def get_book_id(short_name, term):
    """Map clearance book short name to DB book ID."""
    sn = short_name.strip()
    mapping = TERM_A_BOOK_MAP if term == 'A' else TERM_B_BOOK_MAP
    # Direct match
    if sn in mapping:
        return mapping[sn]
    # Normalized match
    nsn = norm(sn)
    for key, bid in mapping.items():
        if norm(key) == nsn or norm(key) in nsn or nsn in norm(key):
            return bid
    return None

def match_library(name_from_doc, gov_folder, city_folder):
    """Match document library name to DB library ID."""
    if not name_from_doc:
        return None
    
    # Clean the name
    name = name_from_doc.strip()
    name = re.sub(r'\s*-\s*(مسقط|محافظة|ولاية).*$', '', name)
    name = re.sub(r'\s*ب(مسقط|صلالة|نزوى|بهلا|صحار|صور).*$', '', name)
    name = name.strip().rstrip('-').strip()
    
    # Try all DB libraries
    best_id = None
    best_score = 0
    
    for lid, lib in db_libs.items():
        db_name = lib['Name']
        # Exact
        if db_name == name or db_name == name_from_doc:
            return lid
        # Core name match (remove مكتبة prefix)
        core_doc = re.sub(r'^(مكتبة|متجر|فاتورة)\s+', '', name).strip()
        core_db = re.sub(r'^(مكتبة|متجر)\s+', '', db_name).strip()
        if core_db == core_doc:
            return lid
        # Normalized
        n1 = norm(core_doc)
        n2 = norm(core_db)
        if n1 == n2:
            return lid
        if n2 and (n2 in n1 or n1 in n2):
            score = len(min(n1, n2, key=len)) / len(max(n1, n2, key=len))
            if score > best_score and score > 0.5:
                best_score = score
                best_id = lid
    
    # Fuzzy match as fallback
    if not best_id:
        all_names = {norm(re.sub(r'^(مكتبة|متجر)\s+', '', lib['Name']).strip()): lid for lid, lib in db_libs.items()}
        core_doc = norm(re.sub(r'^(مكتبة|متجر|فاتورة)\s+', '', name).strip())
        matches = difflib.get_close_matches(core_doc, all_names.keys(), n=1, cutoff=0.55)
        if matches:
            best_id = all_names[matches[0]]
    
    return best_id

# ── Phase 3: Delete broken 2025-2026 invoices ──────────────────────────
try:
    cur.execute("BEGIN TRANSACTION")
    
    # Count before
    cur.execute("SELECT COUNT(*) FROM Invoices WHERE SemesterId IN (3,4)")
    before_inv = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM InvoiceItems WHERE InvoiceId IN (SELECT Id FROM Invoices WHERE SemesterId IN (3,4))")
    before_items = cur.fetchone()[0]
    
    # Delete (both active and inactive)
    cur.execute("PRAGMA foreign_keys = OFF")
    cur.execute("DELETE FROM InvoiceItems WHERE InvoiceId IN (SELECT Id FROM Invoices WHERE SemesterId IN (3,4))")
    cur.execute("DELETE FROM Invoices WHERE SemesterId IN (3,4)")
    cur.execute("PRAGMA foreign_keys = ON")
    
    # Reset autoincrement
    cur.execute("UPDATE sqlite_sequence SET seq = 0 WHERE name = 'Invoices'")
    cur.execute("UPDATE sqlite_sequence SET seq = 0 WHERE name = 'InvoiceItems'")
    
    print(f"✅ Phase 3: Deleted {before_inv} invoices and {before_items} invoice items")
    
    # ── Phase 4: Insert correct data ───────────────────────────────────
    invoice_counter = {}  # (lib_id, sem_id) -> next number
    stats = {'orders_A': 0, 'refunds_A': 0, 'orders_B': 0, 'refunds_B': 0, 
             'items_inserted': 0, 'libs_updated': 0}
    unmatched = []
    matched_map = {}  # For reporting
    
    def next_inv_num(lib_id, sem_id):
        key = (lib_id, sem_id)
        invoice_counter[key] = invoice_counter.get(key, 0) + 1
        return invoice_counter[key]
    
    def create_invoice(lib_id, sem_id, term_code, inv_type, date_str, items_data):
        """Create an invoice with items. items_data = [(book_id, qty), ...]"""
        # Filter valid items
        valid_items = [(bid, qty) for bid, qty in items_data if bid and qty > 0]
        if not valid_items:
            return None
        
        lib = db_libs[lib_id]
        inv_num = next_inv_num(lib_id, sem_id)
        
        cur.execute("""
            INSERT INTO Invoices (
                InvoiceNumber, InvoiceYear, TermCode, Type, LibraryId, LibraryName,
                SemesterId, Date, TotalAmount, PrintStatus, ResponsibleName, 
                ResponsiblePhone, IsActive
            ) VALUES (?, 2025, ?, ?, ?, ?, ?, ?, 0, 'printed', ?, ?, 1)
        """, (inv_num, term_code, inv_type, lib_id, lib['Name'],
              sem_id, date_str, lib.get('ResponsibleName',''), lib.get('ResponsiblePhone','')))
        
        inv_id = cur.lastrowid
        total = 0.0
        
        for bid, qty in valid_items:
            bk = book_info.get(bid)
            if not bk:
                continue
            line_total = qty * bk['Price']
            total += line_total
            cur.execute("""
                INSERT INTO InvoiceItems (InvoiceId, BookId, BookName, BookGrade, Quantity, UnitPrice, Total)
                VALUES (?, ?, ?, ?, ?, ?, ?)
            """, (inv_id, bid, bk['Name'], bk['Grade'], qty, bk['Price'], line_total))
            stats['items_inserted'] += 1
        
        cur.execute("UPDATE Invoices SET TotalAmount = ? WHERE Id = ?", (total, inv_id))
        return inv_id
    
    # Process each document entry
    for entry in data:
        lib_info = entry.get('library_info', {})
        lib_name = lib_info.get('library_name', '')
        if not lib_name:
            # Try filename
            lib_name = entry.get('filename', '').replace('.docx','').strip()
            if not lib_name:
                continue
        
        lib_id = match_library(lib_name, entry.get('governorate',''), entry.get('city',''))
        if not lib_id:
            unmatched.append(f"{lib_name} [{entry.get('governorate','')}/{entry.get('city','')}] ({entry.get('filename','')})")
            continue
        
        matched_map[lib_name] = db_libs[lib_id]['Name']
        
        # Update library contact info
        rn = lib_info.get('responsible_name','')
        rp = lib_info.get('responsible_phone','')
        on = lib_info.get('owner_name','')
        if rn or rp or on:
            updates = []
            params = []
            if rn and not db_libs[lib_id].get('ResponsibleName'):
                updates.append("ResponsibleName = ?"); params.append(rn)
            if rp and not db_libs[lib_id].get('ResponsiblePhone'):
                updates.append("ResponsiblePhone = ?"); params.append(rp)
            if on:
                updates.append("OwnerName = ?"); params.append(on)
            if updates:
                params.append(lib_id)
                cur.execute(f"UPDATE Libraries SET {', '.join(updates)} WHERE Id = ?", params)
                stats['libs_updated'] += 1
        
        # ── Process Term A clearances ──
        for clr in entry.get('term_a_clearances', []):
            clr_items = clr.get('items', [])
            if not clr_items:
                continue
            
            # Build order items (sent quantities)
            order_items = []
            refund_items = []
            for item in clr_items:
                bid = get_book_id(item['book_short'], 'A')
                sent = item.get('sent', 0)
                ret = item.get('returned', 0)
                if bid:
                    if sent > 0:
                        order_items.append((bid, sent))
                    if ret > 0:
                        refund_items.append((bid, ret))
            
            if order_items:
                create_invoice(lib_id, 3, 'A', 'order', '2025-09-01', order_items)
                stats['orders_A'] += 1
            if refund_items:
                create_invoice(lib_id, 3, 'A', 'refund', '2025-12-15', refund_items)
                stats['refunds_A'] += 1
        
        # ── Process Term B clearances ──
        for clr in entry.get('term_b_clearances', []):
            clr_items = clr.get('items', [])
            if not clr_items:
                continue
            
            order_items = []
            refund_items = []
            for item in clr_items:
                bid = get_book_id(item['book_short'], 'B')
                sent = item.get('sent', 0)
                ret = item.get('returned', 0)
                if bid:
                    if sent > 0:
                        order_items.append((bid, sent))
                    if ret > 0:
                        refund_items.append((bid, ret))
            
            if order_items:
                create_invoice(lib_id, 4, 'B', 'order', '2026-02-01', order_items)
                stats['orders_B'] += 1
            if refund_items:
                create_invoice(lib_id, 4, 'B', 'refund', '2026-05-15', refund_items)
                stats['refunds_B'] += 1
        
        # ── If no clearances but has invoices, use invoice data ──
        if not entry.get('term_a_clearances') and entry.get('term_a_invoices'):
            for inv_data in entry['term_a_invoices']:
                items = []
                for item in inv_data:
                    bid = get_book_id(item.get('name','').replace('فيزياء الصف التاسع','فيزياء التاسع')
                                     .replace('كيمياء الصف التاسع','كيمياء التاسع')
                                     .replace('فيزياء الصف العاشر','فيزياء العاشر')
                                     .replace('كيمياء الصف العاشر','كيمياء العاشر')
                                     .replace('فيزياء الحادي عشر (كتابين)','فيزياء الحادي عشر')
                                     .replace('فيزياء الثاني عشر (كتاب واحد)','فيزياء الثاني عشر')
                                     .replace('العلوم البيئية (القسم الأدبي)','العلوم البيئية 11'), 'A')
                    qty = item.get('qty', 0)
                    if bid and qty > 0:
                        items.append((bid, qty))
                if items:
                    create_invoice(lib_id, 3, 'A', 'order', '2025-09-01', items)
                    stats['orders_A'] += 1
        
        if not entry.get('term_b_clearances') and entry.get('term_b_invoices'):
            for inv_data in entry['term_b_invoices']:
                items = []
                for item in inv_data:
                    name_mapped = (item.get('name','')
                        .replace('فيزياء الصف التاسع (كتابين)','فيزياء التاسع')
                        .replace('كيمياء الصف التاسع (كتاب واحد)','كيمياء التاسع')
                        .replace('فيزياء الصف العاشر (كتابين)','فيزياء العاشر')
                        .replace('فيزياء الحادي عشر (كتاب واحد)','فيزياء الحادي عشر')
                        .replace('العلوم البيئية أدبي (كتاب واحد)','العلوم البيئية 12')
                        .replace('فيزياء الثاني عشر (كتابين)','فيزياء الثاني عشر'))
                    bid = get_book_id(name_mapped, 'B')
                    qty = item.get('qty', 0)
                    if bid and qty > 0:
                        items.append((bid, qty))
                if items:
                    create_invoice(lib_id, 4, 'B', 'order', '2026-02-01', items)
                    stats['orders_B'] += 1
    
    # ── Phase 5: Activate Term B ───────────────────────────────────────
    cur.execute("UPDATE Semesters SET IsActive = 1 WHERE Id = 4")
    print("✅ Phase 5: Term B (Semester 4) activated")
    
    # ── Commit ─────────────────────────────────────────────────────────
    conn.commit()
    print("✅ Migration committed successfully!")
    
    # ── Phase 7: Verify ────────────────────────────────────────────────
    print("\n=== VERIFICATION ===")
    cur.execute("""
        SELECT Type, TermCode, COUNT(*) as cnt, SUM(TotalAmount) as total,
               SUM(CASE WHEN IsActive=1 THEN 1 ELSE 0 END) as active
        FROM Invoices WHERE SemesterId IN (3,4) GROUP BY Type, TermCode
    """)
    for row in cur.fetchall():
        print(f"  {row['Type']} Term {row['TermCode']}: {row['cnt']} invoices, total={row['total']:.3f} R.O., active={row['active']}")
    
    cur.execute("SELECT COUNT(*) as cnt FROM InvoiceItems WHERE InvoiceId IN (SELECT Id FROM Invoices WHERE SemesterId IN (3,4))")
    print(f"  Total invoice items: {cur.fetchone()['cnt']}")
    
    cur.execute("SELECT COUNT(DISTINCT LibraryId) as cnt FROM Invoices WHERE SemesterId IN (3,4)")
    print(f"  Libraries with invoices: {cur.fetchone()['cnt']}")
    
    print(f"\n=== STATS ===")
    for k, v in stats.items():
        print(f"  {k}: {v}")
    
    print(f"\n=== MATCHED LIBRARIES ({len(matched_map)}) ===")
    for doc_name, db_name in sorted(matched_map.items()):
        print(f"  '{doc_name}' → '{db_name}'")
    
    if unmatched:
        print(f"\n=== UNMATCHED LIBRARIES ({len(unmatched)}) ===")
        for u in sorted(set(unmatched)):
            print(f"  ❌ {u}")

except Exception as e:
    conn.rollback()
    import traceback
    print(f"❌ ERROR - rolled back: {e}")
    traceback.print_exc()

conn.close()
print("\nDone.")
