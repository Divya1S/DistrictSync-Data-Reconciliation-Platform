using System.Globalization;
using System.Text;

// Generates two CSV snapshots (HR and Payroll) of the same employee population with planted anomalies,
// so reconciliation runs have a known expected outcome. Usage:
//   dotnet run --project tools/DistrictSync.DataGen -- --rows 1000000 --out samples/generated --seed 42
var options = Options.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);

var rng = new Random(options.Seed);
var departments = new[] { "Teaching", "Administration", "Transportation", "Nutrition", "Facilities", "IT", "Counseling", "Athletics" };
var titles = new[] { "Teacher", "Lead Teacher", "Principal", "Bus Driver", "Custodian", "Analyst", "Counselor", "Coach", "Clerk", "Nurse" };
var firstNames = new[] { "Ava", "Liam", "Noah", "Emma", "Olivia", "Mateo", "Sofia", "Aiden", "Priya", "Arjun", "Chen", "Yuki", "Fatima", "Omar", "Zoe", "Ethan", "Maya", "Lucas", "Nia", "Kai" };
var lastNames = new[] { "Lee", "Patel", "Garcia", "Nguyen", "Smith", "Okafor", "Kim", "Rossi", "Khan", "Silva", "Brown", "Ivanov", "Martin", "Haddad", "Cohen", "Diaz", "Yamamoto", "Dubois", "Novak", "Reyes" };

var expected = new Counters();
var baseDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

await using var hr = new StreamWriter(Path.Combine(options.OutputDirectory, "hr_employees.csv"), false, new UTF8Encoding(false), 1 << 16);
await using var payroll = new StreamWriter(Path.Combine(options.OutputDirectory, "payroll_employees.csv"), false, new UTF8Encoding(false), 1 << 16);
const string header = "employee_id,work_email,first_name,last_name,department,job_title,annual_salary,phone,status,hire_date,last_modified";
await hr.WriteLineAsync(header);
await payroll.WriteLineAsync(header);

for (var i = 1; i <= options.Rows; i++)
{
    var first = firstNames[rng.Next(firstNames.Length)];
    var last = lastNames[rng.Next(lastNames.Length)];
    var email = $"{first}.{last}.{i}@northgate.edu".ToLowerInvariant();
    var department = departments[rng.Next(departments.Length)];
    var title = titles[rng.Next(titles.Length)];
    var salary = 32_000 + rng.Next(0, 90_000);
    var phone = $"555-{rng.Next(100, 999)}-{rng.Next(1000, 9999)}";
    var hireDate = baseDate.AddDays(-rng.Next(0, 6000)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var status = rng.NextDouble() < 0.04 ? "Terminated" : "Active";
    var modified = baseDate.AddDays(-rng.Next(0, 400)).ToString("O", CultureInfo.InvariantCulture);

    var hrRow = new Row($"E{i:D7}", email, first, last, department, title, salary, phone, status, hireDate, modified);
    await hr.WriteLineAsync(hrRow.ToCsv());
    expected.HrRows++;

    var roll = rng.NextDouble();
    if (roll < options.MissingRate)
    {
        // Present in HR, absent from payroll (expected for terminated staff; an exception otherwise).
        if (status == "Terminated")
        {
            expected.MissingTerminated++;
        }
        else
        {
            expected.MissingActive++;
        }

        continue;
    }

    var payrollRow = hrRow with { EmployeeId = $"P{i:D7}" };
    roll = rng.NextDouble();
    if (roll < options.ConflictRate)
    {
        // Field conflict: alternate between money (→ case) and contact details (→ auto-resolve).
        var newer = baseDate.AddDays(-rng.Next(0, 30)).ToString("O", CultureInfo.InvariantCulture);
        if (rng.NextDouble() < 0.5)
        {
            payrollRow = payrollRow with { Salary = salary + rng.Next(500, 5000), LastModified = newer };
            expected.SalaryConflicts++;
        }
        else
        {
            payrollRow = payrollRow with { Phone = $"555-{rng.Next(100, 999)}-{rng.Next(1000, 9999)}", Title = titles[rng.Next(titles.Length)], LastModified = newer };
            expected.ContactConflicts++;
        }
    }
    else if (roll < options.ConflictRate + options.FormattingNoiseRate)
    {
        // Same data, different formatting: must reconcile as a match thanks to normalizers.
        payrollRow = payrollRow with { Phone = $"({phone[..3]}) {phone[4..7]}-{phone[8..]}", Department = department.ToUpperInvariant(), FirstName = $" {first} " };
        expected.FormattingOnly++;
    }

    await payroll.WriteLineAsync(payrollRow.ToCsv());
    expected.PayrollRows++;

    if (rng.NextDouble() < options.DuplicateRate)
    {
        await payroll.WriteLineAsync((payrollRow with { EmployeeId = $"P{i:D7}-DUP" }).ToCsv());
        expected.PayrollRows++;
        expected.Duplicates++;
    }

    if (roll < options.ConflictRate + options.FormattingNoiseRate + options.ExtraInPayrollRate && roll >= options.ConflictRate + options.FormattingNoiseRate)
    {
        var ghost = payrollRow with { EmployeeId = $"P{i:D7}-X", Email = $"ghost.{i}@northgate.edu" };
        await payroll.WriteLineAsync(ghost.ToCsv());
        expected.PayrollRows++;
        expected.MissingInHr++;
    }
}

await hr.FlushAsync();
await payroll.FlushAsync();

Console.WriteLine($"Wrote {expected.HrRows:N0} HR rows and {expected.PayrollRows:N0} payroll rows to {options.OutputDirectory}");
Console.WriteLine("Expected reconciliation outcome (HR = left, PAYROLL = right):");
Console.WriteLine($"  MissingInRight (terminated, rule → Ignore): {expected.MissingTerminated:N0}");
Console.WriteLine($"  MissingInRight (active, → case):            {expected.MissingActive:N0}");
Console.WriteLine($"  MissingInLeft (ghost payroll rows):         {expected.MissingInHr:N0}");
Console.WriteLine($"  FieldConflict salary (→ High case):         {expected.SalaryConflicts:N0}");
Console.WriteLine($"  FieldConflict contact (→ auto-resolve):     {expected.ContactConflicts:N0}");
Console.WriteLine($"  Duplicates in payroll (→ case):             {expected.Duplicates:N0}");
Console.WriteLine($"  Formatting-only differences (→ match):      {expected.FormattingOnly:N0}");

internal sealed record Row(string EmployeeId, string Email, string FirstName, string LastName, string Department, string Title, int Salary, string Phone, string Status, string HireDate, string LastModified)
{
    public string ToCsv() => string.Join(',', EmployeeId, Email, Quote(FirstName), Quote(LastName), Quote(Department), Quote(Title), Salary.ToString(CultureInfo.InvariantCulture), Quote(Phone), Status, HireDate, LastModified);

    private static string Quote(string value) => value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.StartsWith(' ') || value.EndsWith(' ')
        ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
        : value;
}

internal sealed class Counters
{
    public long HrRows { get; set; }
    public long PayrollRows { get; set; }
    public long MissingTerminated { get; set; }
    public long MissingActive { get; set; }
    public long MissingInHr { get; set; }
    public long SalaryConflicts { get; set; }
    public long ContactConflicts { get; set; }
    public long Duplicates { get; set; }
    public long FormattingOnly { get; set; }
}

internal sealed record Options(int Rows, string OutputDirectory, int Seed, double MissingRate, double ConflictRate, double DuplicateRate, double FormattingNoiseRate, double ExtraInPayrollRate)
{
    public static Options Parse(string[] args)
    {
        var rows = 100_000;
        var output = "samples/generated";
        var seed = 42;
        double missing = 0.01, conflict = 0.02, duplicate = 0.002, formatting = 0.05, extra = 0.003;
        for (var i = 0; i < args.Length - 1; i += 2)
        {
            var value = args[i + 1];
            switch (args[i])
            {
                case "--rows":
                    rows = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--out":
                    output = value;
                    break;
                case "--seed":
                    seed = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--missing-rate":
                    missing = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--conflict-rate":
                    conflict = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--duplicate-rate":
                    duplicate = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--formatting-rate":
                    formatting = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--extra-rate":
                    extra = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return new Options(rows, output, seed, missing, conflict, duplicate, formatting, extra);
    }
}
