using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Import;

/// <summary>How a column's text is read. Decides which leading characters are legitimate.</summary>
public enum ImportColumnKind
{
    Text,
    Email,
    Phone,
    Date,
    Integer,
    Boolean
}

public sealed record ImportColumn(string Key, string Header, bool Required, ImportColumnKind Kind, string Description, string? Example, int? MaxLength)
{
    public ImportColumnDto ToDto() => new(Key, Header, Required, Description, Example, MaxLength);
}

public sealed record ImportCategoryDefinition(
    ImportCategory Category, string Name, string Description, int MaxRows,
    IReadOnlyList<ImportColumn> Columns, IReadOnlyList<string> Notes)
{
    public ImportColumn? Column(string key) => Columns.FirstOrDefault(c => c.Key == key);

    public ImportCategoryDto ToDto() => new(Category, Name, Description, MaxRows, Columns.Select(c => c.ToDto()).ToList(), Notes);
}

/// <summary>
/// The columns each bulk-import category accepts. Related records are referenced by the
/// natural keys an admin can see on screen (guardian email, bus number, route name, driver
/// licence number), never by internal database ids. Lengths match the single-record validators.
/// </summary>
public static class ImportSchema
{
    public const int DefaultMaxRows = 5000;
    /// <summary>
    /// Every imported guardian gets a hashed random password and a credential email, both done
    /// one by one, so a guardian file is kept small enough to finish inside one request.
    /// </summary>
    public const int GuardianMaxRows = 200;

    public static class Keys
    {
        public const string FullName = "fullName";
        public const string DateOfBirth = "dateOfBirth";
        public const string Grade = "grade";
        public const string GuardianEmail = "guardianEmail";
        public const string Relationship = "relationship";
        public const string AuthorizedForPickup = "authorizedForPickup";
        public const string BusNumber = "busNumber";
        public const string RfidCardUid = "rfidCardUid";
        public const string Email = "email";
        public const string ContactNumber = "contactNumber";
        public const string Phone = "phone";
        public const string LicenseNumber = "licenseNumber";
        public const string RouteName = "routeName";
        public const string Description = "description";
        public const string Capacity = "capacity";
        public const string DriverLicenseNumber = "driverLicenseNumber";
        // RouteStops
        public const string StopName = "stopName";
        public const string Address = "address";
        public const string PickupTime = "pickupTime";
        public const string DropTime = "dropTime";
        // SchoolCalendarDays
        public const string Date = "date";
        public const string DayType = "dayType";
        public const string Title = "title";
        public const string StartTime = "startTime";
        public const string LateArrivalAfter = "lateArrivalAfter";
        public const string DismissalTime = "dismissalTime";
    }

    public static readonly ImportCategoryDefinition Students = new(
        ImportCategory.Students, "Students",
        "Student records, optionally linked to an existing guardian, bus and a new RFID card in the same row.",
        DefaultMaxRows,
        new[]
        {
            new ImportColumn(Keys.FullName, "Full Name", true, ImportColumnKind.Text, "Student's full name.", "Aarav Sharma", 150),
            new ImportColumn(Keys.DateOfBirth, "Date of Birth", true, ImportColumnKind.Date, "YYYY-MM-DD (DD-MM-YYYY and DD/MM/YYYY are also read). Must be in the past.", "2016-04-21", null),
            new ImportColumn(Keys.Grade, "Grade", false, ImportColumnKind.Text, "Class or grade.", "Grade 3", 50),
            new ImportColumn(Keys.GuardianEmail, "Guardian Email", false, ImportColumnKind.Email, "Email of a guardian already registered in ChildGuard. Links the student to that guardian.", "parent@example.com", 200),
            new ImportColumn(Keys.Relationship, "Relationship", false, ImportColumnKind.Text, "Guardian's relationship to the student. Needs Guardian Email.", "Mother", 50),
            new ImportColumn(Keys.AuthorizedForPickup, "Authorized For Pickup", false, ImportColumnKind.Boolean, "Yes or No (default No). Needs Guardian Email.", "Yes", null),
            new ImportColumn(Keys.BusNumber, "Bus Number", false, ImportColumnKind.Text, "Number of an existing, in-service bus with a free seat. Creates the student's bus assignment on that bus's route.", "BUS-12", 20),
            new ImportColumn(Keys.RfidCardUid, "RFID Card UID", false, ImportColumnKind.Text, "UID of a new RFID card to issue to the student. Must not be registered yet.", "04A1B2C3D4", 100),
        },
        new[]
        {
            "A student with the same full name and date of birth as an existing student is reported as a duplicate.",
            "Import guardians first: Guardian Email must belong to a guardian who is already registered.",
            "Import order for transport: drivers, then routes, then buses, then students.",
            "A bus number must belong to a bus in service with a free seat for every student this file puts on it.",
        });

    public static readonly ImportCategoryDefinition Guardians = new(
        ImportCategory.Guardians, "Guardians",
        "Guardian accounts. Each guardian gets their own random starting password by email and must change it at first sign-in.",
        GuardianMaxRows,
        new[]
        {
            new ImportColumn(Keys.FullName, "Full Name", true, ImportColumnKind.Text, "Guardian's full name.", "Priya Sharma", 150),
            new ImportColumn(Keys.Email, "Email", true, ImportColumnKind.Email, "Where the sign-in details are sent. Must not be registered yet.", "priya@example.com", 200),
            new ImportColumn(Keys.ContactNumber, "Contact Number", true, ImportColumnKind.Phone, "Mobile number; the guardian signs in with it. Must not be registered yet.", "+91 98200 11111", 30),
        },
        new[]
        {
            $"At most {GuardianMaxRows} guardians per import, because each one is sent a credential email.",
            "Email and contact number must be unique, both within the file and against existing guardians.",
        });

    public static readonly ImportCategoryDefinition Drivers = new(
        ImportCategory.Drivers, "Drivers",
        "Bus drivers.",
        DefaultMaxRows,
        new[]
        {
            new ImportColumn(Keys.FullName, "Full Name", true, ImportColumnKind.Text, "Driver's full name.", "Ramesh Kumar", 150),
            new ImportColumn(Keys.Phone, "Phone", false, ImportColumnKind.Phone, "Contact number.", "+91 98765 43210", 30),
            new ImportColumn(Keys.LicenseNumber, "License Number", false, ImportColumnKind.Text, "Driving licence number. Must be unique; buses refer to drivers by it.", "MH12 20110012345", 50),
        },
        new[] { "A driver with the same licence number, or the same name and phone, as an existing driver is reported as a duplicate." });

    public static readonly ImportCategoryDefinition Routes = new(
        ImportCategory.Routes, "Routes",
        "Bus routes.",
        DefaultMaxRows,
        new[]
        {
            new ImportColumn(Keys.RouteName, "Route Name", true, ImportColumnKind.Text, "Unique route name.", "North Loop", 100),
            new ImportColumn(Keys.Description, "Description", false, ImportColumnKind.Text, "Stops or notes.", "Via Market Road and Station", 500),
        },
        new[] { "Route names must be unique (not case-sensitive)." });

    public static readonly ImportCategoryDefinition Buses = new(
        ImportCategory.Buses, "Buses",
        "Buses, optionally with an existing driver and route.",
        DefaultMaxRows,
        new[]
        {
            new ImportColumn(Keys.BusNumber, "Bus Number", true, ImportColumnKind.Text, "Unique bus number.", "BUS-12", 20),
            new ImportColumn(Keys.Capacity, "Capacity", true, ImportColumnKind.Integer, "Seats, 1 to 200.", "40", null),
            new ImportColumn(Keys.DriverLicenseNumber, "Driver License Number", false, ImportColumnKind.Text, "Licence number of an existing driver.", "MH12 20110012345", 50),
            new ImportColumn(Keys.RouteName, "Route Name", false, ImportColumnKind.Text, "Name of an existing route.", "North Loop", 100),
        },
        new[] { "Import drivers and routes first: they must already exist and be active." });

    public static readonly ImportCategoryDefinition RouteStops = new(
        ImportCategory.RouteStops, "Route Stops",
        "Stops along existing routes, in import order. Each stop is appended after the route's last existing stop.",
        DefaultMaxRows,
        new[]
        {
            new ImportColumn(Keys.RouteName, "Route Name", true, ImportColumnKind.Text, "Name of an existing, active route.", "North Loop", 100),
            new ImportColumn(Keys.StopName, "Stop Name", true, ImportColumnKind.Text, "Name of the stop.", "Market Road Junction", 100),
            new ImportColumn(Keys.Address, "Address", false, ImportColumnKind.Text, "Street address or landmark.", "Market Road, Sector 5", 200),
            new ImportColumn(Keys.PickupTime, "Pickup Time", false, ImportColumnKind.Text, "Morning pickup time at this stop (HH:MM, 24-hour). Example: 07:30", "07:30", null),
            new ImportColumn(Keys.DropTime, "Drop Time", false, ImportColumnKind.Text, "Afternoon drop-off time at this stop (HH:MM, 24-hour). Example: 15:45", "15:45", null),
        },
        new[]
        {
            "Route must already exist and be active. Import routes first.",
            "Stops within the same route are appended in row order, after any stops already on the route.",
            "A stop with the same name on the same route (not case-sensitive) is reported as a duplicate.",
        });

    public static readonly ImportCategoryDefinition SchoolCalendarDays = new(
        ImportCategory.SchoolCalendarDays, "School Calendar Days",
        "Calendar exceptions: holidays, half days, special timings, and extra working days.",
        DefaultMaxRows,
        new[]
        {
            new ImportColumn(Keys.Date, "Date", true, ImportColumnKind.Date, "YYYY-MM-DD. Must be within 2 years of today.", "2027-01-26", null),
            new ImportColumn(Keys.DayType, "Day Type", true, ImportColumnKind.Text, "Holiday, HalfDay, SpecialTiming, or WorkingDay.", "Holiday", 20),
            new ImportColumn(Keys.Title, "Title", true, ImportColumnKind.Text, "Label shown to admins and guardians (e.g. Republic Day).", "Republic Day", 100),
            new ImportColumn(Keys.DismissalTime, "Dismissal Time", false, ImportColumnKind.Text, "Required for HalfDay and SpecialTiming. HH:MM, 24-hour.", "12:00", null),
            new ImportColumn(Keys.StartTime, "Start Time", false, ImportColumnKind.Text, "Override the regular start time (SpecialTiming or WorkingDay). HH:MM.", "09:30", null),
            new ImportColumn(Keys.LateArrivalAfter, "Late Arrival After", false, ImportColumnKind.Text, "Override the regular late cutoff. HH:MM.", "10:00", null),
        },
        new[]
        {
            "Holiday rows must not include any times.",
            "HalfDay rows require Dismissal Time.",
            "SpecialTiming rows require at least one of Dismissal Time, Start Time, or Late Arrival After.",
            "One row per date; importing a date that already has an active exception is reported as a duplicate.",
            "Dates must be within 2 years of today.",
        });

    public static IReadOnlyList<ImportCategoryDefinition> All { get; } = new[] { Students, Guardians, Drivers, Routes, Buses, RouteStops, SchoolCalendarDays };

    public static ImportCategoryDefinition? Find(ImportCategory category) => All.FirstOrDefault(d => d.Category == category);
}
