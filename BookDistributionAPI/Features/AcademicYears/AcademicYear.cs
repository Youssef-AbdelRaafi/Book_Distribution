namespace BookDistributionAPI.Features.AcademicYears;

using System.ComponentModel.DataAnnotations;

public class AcademicYear
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; 
    public bool IsActive { get; set; }
    public ICollection<Semesters.Semester> Semesters { get; set; } = new List<Semesters.Semester>();
}

public class CreateAcademicYearDto
{
    [Required, RegularExpression("^20\\d{2}-20\\d{2}$")]
    public string Name { get; set; } = string.Empty;
}
