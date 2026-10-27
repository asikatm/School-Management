using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Exams;
using SchoolManagement.Api.Modules.Learning;
using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Exams;
using SchoolManagement.Domain.Learning;

namespace SchoolManagement.Tests;

public class LearningAndExamTests
{
    private static readonly GradeScale Gpa5 = new(Guid.NewGuid(), "GPA5", "Local GPA 5.00", GradingScaleType.Gpa5,
    [
        new("A+", null, null, 80, 100, 5.00m, true), new("A", null, null, 70, 79.99m, 4.00m, true), new("A-", null, null, 60, 69.99m, 3.50m, true),
        new("B", null, null, 50, 59.99m, 3.00m, true), new("C", null, null, 40, 49.99m, 2.00m, true), new("D", null, null, 33, 39.99m, 1.00m, true),
        new("F", null, null, 0, 32.99m, 0.00m, false)
    ]);

    // ---------------------------------------------------------------- LRN-02 homework

    [Theory]
    [InlineData(18, false, 10, 18)]   // on time: no penalty
    [InlineData(18, true, 10, 16.2)]  // late: 10% off the marks given
    [InlineData(18, true, 0, 18)]     // late, but the homework has no penalty
    public void Late_penalty_reduces_the_marks_given(double marks, bool late, double penalty, double expected) =>
        Assert.Equal((decimal)expected, HomeworkService.FinalMarks((decimal)marks, late, (decimal)penalty));

    [Fact]
    public void Ungraded_homework_has_no_final_marks() => Assert.Null(HomeworkService.FinalMarks(null, true, 10));

    // ---------------------------------------------------------------- LRN-08 gradebook

    private static readonly CategoryView Classwork = new(Guid.NewGuid(), null, "Classwork", 60, 0);
    private static readonly CategoryView Quizzes = new(Guid.NewGuid(), null, "Quizzes", 40, 1);

    private static GradebookItem Item(CategoryView c, decimal max) =>
        new() { Title = c.Name, CategoryId = c.Id, MaxMarks = max };

    [Fact]
    public void Term_percent_weights_each_category()
    {
        var cw1 = Item(Classwork, 10);
        var cw2 = Item(Classwork, 10);
        var quiz = Item(Quizzes, 20);
        var marks = new Dictionary<Guid, MarkCell> { [cw1.Id] = new(8, false, null), [cw2.Id] = new(6, false, null), [quiz.Id] = new(10, false, null) };

        var (byCategory, total) = GradebookService.Calculate([Classwork, Quizzes], [cw1, cw2, quiz], marks);

        Assert.Equal(70m, byCategory[Classwork.Id]); // 14/20
        Assert.Equal(50m, byCategory[Quizzes.Id]);   // 10/20
        Assert.Equal(62m, total);                    // 0.6×70 + 0.4×50
    }

    [Fact]
    public void Excused_and_unmarked_work_is_left_out_and_weights_renormalise()
    {
        var cw = Item(Classwork, 10);
        var quiz = Item(Quizzes, 20);
        var marks = new Dictionary<Guid, MarkCell> { [cw.Id] = new(9, false, null), [quiz.Id] = new(null, true, "Sick") };

        var (_, total) = GradebookService.Calculate([Classwork, Quizzes], [cw, quiz], marks);

        Assert.Equal(90m, total); // only classwork counts
    }

    [Fact]
    public void A_student_with_nothing_marked_has_no_term_percent() =>
        Assert.Null(GradebookService.Calculate([Classwork], [Item(Classwork, 10)], new Dictionary<Guid, MarkCell>()).Total);

    // ---------------------------------------------------------------- EXM-03 subject %

    [Fact]
    public void Subject_percent_uses_component_weights()
    {
        // Written 64/80 at 70%, practical 18/20 at 30% → 0.7×80 + 0.3×90 = 83
        var (pct, code) = MarkService.SubjectPercent([new(80, 70, 64, MarkCode.None), new(20, 30, 18, MarkCode.None)]);
        Assert.Equal(83m, pct);
        Assert.Null(code);
    }

    [Fact]
    public void Exempt_components_are_dropped_and_absent_counts_zero()
    {
        Assert.Equal(90m, MarkService.SubjectPercent([new(80, 70, 72, MarkCode.None), new(20, 30, null, MarkCode.Exempt)]).Percent);
        Assert.Equal(56m, MarkService.SubjectPercent([new(80, 70, 64, MarkCode.None), new(20, 30, null, MarkCode.Absent)]).Percent);
    }

    [Fact]
    public void Fully_absent_or_exempt_subjects_carry_a_code()
    {
        Assert.Equal((0m, "AB"), MarkService.SubjectPercent([new(100, 100, null, MarkCode.Absent)]));
        Assert.Equal((null, "EX"), MarkService.SubjectPercent([new(100, 100, null, MarkCode.Exempt)]));
    }

    [Fact]
    public void Missing_marks_leave_the_subject_incomplete() =>
        Assert.Equal((null, null), MarkService.SubjectPercent([new(80, 70, 64, MarkCode.None), new(20, 30, null, MarkCode.None)]));

    // ---------------------------------------------------------------- EXM-04 grading

    [Theory]
    [InlineData(85, "A+")]
    [InlineData(80, "A+")]
    [InlineData(79.99, "A")]
    [InlineData(79.991, "A")]  // inside the 79.99–80 gap: takes the band below
    [InlineData(33, "D")]
    [InlineData(10, "F")]
    public void Percent_maps_to_the_band_holding_it(double percent, string expected) =>
        Assert.Equal(expected, Gpa5.BandFor((decimal)percent)!.Grade);

    [Fact]
    public void Gpa_is_the_mean_grade_point()
    {
        Assert.Equal(4.50m, Grading.Gpa([(5.00m, true), (4.00m, true)], zeroOnFail: true));
        Assert.Null(Grading.Gpa([(null, true)], zeroOnFail: true));
    }

    [Fact]
    public void A_failed_subject_makes_gpa_zero_under_the_local_rule()
    {
        Assert.Equal(0m, Grading.Gpa([(5.00m, true), (0.00m, false)], zeroOnFail: true));
        Assert.Equal(2.50m, Grading.Gpa([(5.00m, true), (0.00m, false)], zeroOnFail: false));
    }

    // ---------------------------------------------------------------- EXM-06 ranks

    private static readonly Guid SectionA = Guid.NewGuid(), SectionB = Guid.NewGuid(), Campus = Guid.NewGuid(), YearGroup = Guid.NewGuid();

    private static ResultRow Row(string name, Guid section, decimal percent, bool complete = true) =>
        new(Guid.NewGuid(), name, name, null, section, section == SectionA ? "A" : "B", Campus, YearGroup, percent, null, null, true, 0, null, null, null, null,
            [new SubjectResult(Guid.Empty, "MATH", "Mathematics", [], percent, null, null, null, null, true, null, null)], complete);

    [Fact]
    public void Ties_share_a_place_and_the_next_place_is_skipped()
    {
        var ranked = ResultService.Rank([Row("a", SectionA, 90), Row("b", SectionA, 85), Row("c", SectionA, 85), Row("d", SectionA, 70)]);
        Assert.Equal([1, 2, 2, 4], ranked.Select(r => r.SectionRank!.Value));
    }

    [Fact]
    public void Class_rank_spans_sections_and_section_rank_does_not()
    {
        var ranked = ResultService.Rank([Row("a", SectionA, 80), Row("b", SectionB, 90)]);
        var a = ranked.Single(r => r.Name == "a");
        Assert.Equal(1, a.SectionRank);
        Assert.Equal(2, a.ClassRank);
        Assert.Equal(0m, a.Percentile);
        Assert.Equal(100m, ranked.Single(r => r.Name == "b").Percentile);
    }

    [Fact]
    public void Incomplete_results_are_not_ranked_and_highest_marks_are_per_section()
    {
        var ranked = ResultService.Rank([Row("a", SectionA, 60), Row("b", SectionA, 75), Row("c", SectionA, 0, complete: false)]);
        Assert.Null(ranked.Single(r => r.Name == "c").SectionRank);
        Assert.All(ranked, r => Assert.Equal(75m, r.Subjects[0].HighestInSection));
    }

    // ---------------------------------------------------------------- EXM-02 seating

    [Fact]
    public void Seating_interleaves_sections_so_neighbours_differ()
    {
        SectionInfo S(string label) => new(Guid.NewGuid(), Campus, Guid.Empty, YearGroup, null, label, null, null);
        var a = S("Year 7 - A");
        var b = S("Year 7 - B");
        var candidates = new[] { (a, 1), (a, 2), (a, 3), (b, 1), (b, 2) }
            .Select(x => new Candidate(Guid.NewGuid(), $"{x.Item1.Label}-{x.Item2}", $"{x.Item1.Label} {x.Item2}", x.Item2, x.Item1, [])).ToList();

        var order = ExamService.Interleave(candidates).Select(c => c.Section.Label).ToList();

        Assert.Equal(["Year 7 - A", "Year 7 - B", "Year 7 - A", "Year 7 - B", "Year 7 - A"], order);
    }
}
