using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Data;

/// <summary>Creates the SQLite database on first run and fills it with starter data.</summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// true  = also create ~6 months of sample sales so charts and reports have data (good for testing / defense).
    /// false = start with only categories, suppliers, products and the two default accounts.
    /// Admins can also remove sample transactions later in Settings → "Start fresh".
    /// </summary>
    public const bool SeedDemoSales = true;

    public static void Initialize(IDbContextFactory<AppDbContext> factory)
    {
        using var db = factory.CreateDbContext();

        // NOTE: EnsureCreated is simple and works offline without tools. If you change the
        // entity classes after the database exists, either delete the .db file (dev only) or
        // switch to EF Core Migrations (see README).
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        if (!db.Settings.Any())
        {
            db.Settings.Add(new StoreSettings());
            db.SaveChanges();
        }

        if (!db.Users.Any())
        {
            db.Users.AddRange(
                new User { FullName = "System Administrator", Username = "admin", Role = UserRole.Admin, PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123") },
                new User { FullName = "Store Staff", Username = "staff", Role = UserRole.Staff, PasswordHash = BCrypt.Net.BCrypt.HashPassword("staff123") });
            db.SaveChanges();
        }

        if (!db.Categories.Any())
            SeedCatalog(db);

        if (SeedDemoSales && !db.Sales.Any())
            SeedSales(db);
    }

    private static void SeedCatalog(AppDbContext db)
    {
        var cats = new[]
        {
            new Category { Name = "Laptops", Icon = "laptop", Color = "#E11D2A", Description = "Gaming, business and student laptops" },
            new Category { Name = "Processors", Icon = "cpu", Color = "#B91C1C", Description = "Intel and AMD desktop CPUs" },
            new Category { Name = "Graphics Cards", Icon = "gpu", Color = "#111111", Description = "NVIDIA GeForce and AMD Radeon GPUs" },
            new Category { Name = "Memory (RAM)", Icon = "ram", Color = "#DC2626", Description = "DDR4 / DDR5 desktop and laptop memory" },
            new Category { Name = "Storage", Icon = "hdd", Color = "#374151", Description = "NVMe / SATA SSDs and hard drives" },
            new Category { Name = "Motherboards", Icon = "board", Color = "#991B1B", Description = "Intel and AMD motherboards" },
            new Category { Name = "Monitors", Icon = "monitor", Color = "#1F2937", Description = "Gaming and office displays" },
            new Category { Name = "Peripherals", Icon = "keyboard", Color = "#EF4444", Description = "Keyboards, mice, headsets, webcams" },
            new Category { Name = "Networking", Icon = "wifi", Color = "#4B5563", Description = "Routers, switches and Wi-Fi adapters" },
            new Category { Name = "Power & Cooling", Icon = "power", Color = "#7F1D1D", Description = "PSUs, UPS, AVR, fans and coolers" },
        };
        db.Categories.AddRange(cats);

        var sups = new[]
        {
            new Supplier { Name = "Mindanao Tech Distributors", ContactPerson = "Ramon Villareal", Phone = "0917-555-0101", Email = "orders@mindanaotech.ph", Address = "Davao City", PaymentTerms = "30 days" },
            new Supplier { Name = "CompuParts Wholesale PH", ContactPerson = "Grace Tan", Phone = "0918-555-0202", Email = "sales@compuparts.ph", Address = "Quezon City", PaymentTerms = "COD" },
            new Supplier { Name = "NetLink Supply Co.", ContactPerson = "Paolo Garcia", Phone = "0919-555-0303", Email = "support@netlink.ph", Address = "Cebu City", PaymentTerms = "15 days" },
            new Supplier { Name = "PeriphZone Trading", ContactPerson = "Liza Mendoza", Phone = "0920-555-0404", Email = "hello@periphzone.ph", Address = "Cagayan de Oro City", PaymentTerms = "COD" },
        };
        db.Suppliers.AddRange(sups);
        db.SaveChanges();

        Product P(string sku, string barcode, string name, int cat, string brand, int sup, decimal cost, decimal price, int stock, int reorder) => new()
        {
            Sku = sku, Barcode = barcode, Name = name, CategoryId = cats[cat].Id, Brand = brand, SupplierId = sups[sup].Id,
            CostPrice = cost, SellingPrice = price, Stock = stock, ReorderLevel = reorder, CreatedAt = DateTime.Now.AddDays(-200)
        };

        db.Products.AddRange(
            P("LP-ASU-X1504", "4800000000011", "ASUS VivoBook 15 X1504 i5 / 8GB / 512GB", 0, "ASUS", 0, 26500, 32995, 6, 3),
            P("LP-LEN-SLIM3", "4800000000028", "Lenovo IdeaPad Slim 3 Ryzen 5 / 16GB / 512GB", 0, "Lenovo", 0, 28000, 34995, 2, 3),
            P("LP-ACR-NITRV", "4800000000035", "Acer Nitro V 15 i5 / RTX 4050 / 16GB", 0, "Acer", 0, 52000, 61995, 4, 2),
            P("CPU-AMD-7600", "4800000000042", "AMD Ryzen 5 7600 (6C/12T, AM5)", 1, "AMD", 1, 10200, 12495, 9, 4),
            P("CPU-INT-14400F", "4800000000059", "Intel Core i5-14400F (10C/16T, LGA1700)", 1, "Intel", 1, 9800, 11995, 3, 4),
            P("GPU-MSI-4060", "4800000000066", "MSI GeForce RTX 4060 Ventus 2X 8GB", 2, "MSI", 1, 17500, 20495, 5, 6),
            P("RAM-KNG-D5-16", "4800000000073", "Kingston FURY Beast 16GB DDR5 5600", 3, "Kingston", 1, 2600, 3395, 3, 8),
            P("RAM-TFG-D4-8", "4800000000080", "TeamGroup T-Force Vulcan 8GB DDR4 3200", 3, "TeamGroup", 1, 950, 1295, 0, 6),
            P("SSD-SAM-980-1T", "4800000000097", "Samsung 980 1TB NVMe M.2 SSD", 4, "Samsung", 1, 4200, 5295, 2, 5),
            P("HDD-SEA-2T", "4800000000103", "Seagate Barracuda 2TB 7200RPM HDD", 4, "Seagate", 1, 3100, 3795, 9, 3),
            P("MB-MSI-B650M", "4800000000110", "MSI PRO B650M-A WiFi (AM5)", 5, "MSI", 1, 8700, 10495, 5, 3),
            P("MB-GIG-H610M", "4800000000127", "Gigabyte H610M S2H DDR4 (LGA1700)", 5, "Gigabyte", 1, 3900, 4795, 8, 3),
            P("MON-AOC-24G2", "4800000000134", "AOC 24G2SP 24\" IPS 165Hz Monitor", 6, "AOC", 0, 7200, 8795, 7, 3),
            P("PER-LOG-G102", "4800000000141", "Logitech G102 Lightsync Gaming Mouse", 7, "Logitech", 3, 750, 1095, 35, 10),
            P("PER-RKK-ILIS", "4800000000158", "Rakk Ilis Mechanical Keyboard", 7, "Rakk", 3, 1300, 1795, 9, 10),
            P("PER-A4T-PK910", "4800000000165", "A4Tech PK-910H 1080p Webcam", 7, "A4Tech", 3, 1100, 1495, 11, 3),
            P("PER-GEN-HUB4", "4800000000172", "4-Port USB 3.0 Hub", 7, "Generic", 3, 280, 450, 18, 5),
            P("NET-TPL-C6", "4800000000189", "TP-Link Archer C6 AC1200 Router", 8, "TP-Link", 2, 1350, 1795, 11, 5),
            P("NET-TPL-SG108", "4800000000196", "TP-Link TL-SG108 8-Port Gigabit Switch", 8, "TP-Link", 2, 1050, 1395, 7, 3),
            P("PSU-COR-650", "4800000000202", "Corsair CV650 650W 80+ Bronze PSU", 9, "Corsair", 1, 2900, 3595, 6, 3),
            P("COOL-ARC-MX4", "4800000000219", "Arctic MX-4 Thermal Paste 4g", 9, "Arctic", 1, 280, 420, 24, 5),
            P("UPS-APC-650", "4800000000226", "APC Back-UPS 650VA", 9, "APC", 1, 2600, 3295, 6, 2));
        db.SaveChanges();
    }

    private static void SeedSales(AppDbContext db)
    {
        var rnd = new Random(20261005);
        var products = db.Products.OrderBy(p => p.Id).ToList();
        var bySku = products.ToDictionary(p => p.Sku);
        var admin = db.Users.First(u => u.Role == UserRole.Admin);
        var staff = db.Users.First(u => u.Role == UserRole.Staff);

        // How popular each product is (higher = sells more). Some items stop selling a while ago
        // so the Slow-Moving / Dead Stock report has something to show.
        var weights = new Dictionary<string, (int Weight, int NoSalesInLastDays)>
        {
            ["PER-LOG-G102"] = (14, 0), ["RAM-KNG-D5-16"] = (10, 0), ["SSD-SAM-980-1T"] = (9, 0),
            ["GPU-MSI-4060"] = (7, 0), ["CPU-AMD-7600"] = (6, 0), ["RAM-TFG-D4-8"] = (6, 0),
            ["LP-ASU-X1504"] = (4, 0), ["LP-LEN-SLIM3"] = (3, 0), ["MON-AOC-24G2"] = (4, 0),
            ["CPU-INT-14400F"] = (4, 0), ["NET-TPL-C6"] = (4, 0), ["PSU-COR-650"] = (3, 0),
            ["MB-MSI-B650M"] = (3, 0), ["MB-GIG-H610M"] = (2, 0), ["LP-ACR-NITRV"] = (2, 0),
            ["PER-RKK-ILIS"] = (2, 20), ["COOL-ARC-MX4"] = (1, 45),
            ["HDD-SEA-2T"] = (1, 95), ["NET-TPL-SG108"] = (1, 120), ["PER-A4T-PK910"] = (1, 150),
            ["UPS-APC-650"] = (0, 999), ["PER-GEN-HUB4"] = (0, 999),
        };
        var customers = new[] { "Walk-in", "Walk-in", "Walk-in", "Walk-in", "Jerome Abad", "Kristine Lao", "Miguel Ramos", "Paula Uy", "Carlo Bautista", "Ivy Santos", "Mark Dela Cruz", "Ana Lim" };

        var sold = products.ToDictionary(p => p.Id, _ => 0);
        var sales = new List<Sale>();
        const int days = 180;

        for (int d = days; d >= 0; d--)
        {
            var day = DateTime.Today.AddDays(-d);
            int count = rnd.Next(1, 4) + (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 2 : 0) + (d < 60 ? 1 : 0);
            if (d == 0) count = Math.Min(count, Math.Max(1, DateTime.Now.Hour - 8)); // don't create sales "in the future" today

            for (int s = 0; s < count; s++)
            {
                var pool = products.Where(p => weights.TryGetValue(p.Sku, out var w) && w.Weight > 0 && d >= w.NoSalesInLastDays)
                                   .SelectMany(p => Enumerable.Repeat(p, weights[p.Sku].Weight)).ToList();
                if (pool.Count == 0) continue;

                var time = day.AddHours(9 + rnd.Next(0, 10)).AddMinutes(rnd.Next(0, 60));
                if (time > DateTime.Now) time = DateTime.Now.AddMinutes(-rnd.Next(5, 90));

                var sale = new Sale
                {
                    Date = time,
                    CustomerName = customers[rnd.Next(customers.Length)],
                    CashierId = rnd.NextDouble() < 0.6 ? staff.Id : admin.Id,
                    PaymentMethod = rnd.NextDouble() < 0.7 ? PaymentMethod.Cash : PaymentMethod.GCash,
                };
                int lines = rnd.Next(1, 4);
                for (int l = 0; l < lines; l++)
                {
                    var p = pool[rnd.Next(pool.Count)];
                    if (sale.Items.Any(i => i.ProductId == p.Id)) continue;
                    int qty = p.SellingPrice > 5000 ? 1 : rnd.Next(1, 3);
                    sale.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Sku = p.Sku, Quantity = qty, UnitPrice = p.SellingPrice, UnitCost = p.CostPrice });
                    sold[p.Id] += qty;
                }
                sale.Subtotal = sale.Items.Sum(i => i.UnitPrice * i.Quantity);
                sale.Discount = rnd.NextDouble() < 0.15 ? Math.Round(sale.Subtotal * 0.05m, 2) : 0;
                sale.Total = sale.Subtotal - sale.Discount;
                if (sale.PaymentMethod == PaymentMethod.Cash)
                    sale.AmountTendered = Math.Ceiling(sale.Total / 500m) * 500m;
                else
                {
                    sale.AmountTendered = sale.Total;
                    sale.ReferenceNo = rnd.NextInt64(1_000_000_000, 9_999_999_999).ToString();
                }
                sale.Change = sale.AmountTendered - sale.Total;
                sales.Add(sale);
            }
        }

        sales = sales.OrderBy(s => s.Date).ToList();
        for (int i = 0; i < sales.Count; i++) sales[i].InvoiceNo = $"OR-{i + 1:000000}";

        // Opening stock = current stock + everything sold, received from the supplier before the first sale.
        var running = new Dictionary<int, int>();
        var movements = new List<StockMovement>();
        var openDate = DateTime.Today.AddDays(-days - 5).AddHours(9);
        foreach (var p in products)
        {
            int opening = p.Stock + sold[p.Id];
            running[p.Id] = opening;
            movements.Add(new StockMovement { Date = openDate, Type = MovementType.StockIn, ProductId = p.Id, Quantity = opening, StockAfter = opening, Reference = $"SI-OPEN-{p.Id:000}", SupplierId = p.SupplierId, UnitCost = p.CostPrice, UserId = admin.Id, Notes = "Opening stock" });
        }

        db.Sales.AddRange(sales);
        db.SaveChanges();

        foreach (var s in sales)
            foreach (var i in s.Items)
            {
                running[i.ProductId] -= i.Quantity;
                movements.Add(new StockMovement { Date = s.Date, Type = MovementType.Sale, ProductId = i.ProductId, Quantity = -i.Quantity, StockAfter = running[i.ProductId], Reference = s.InvoiceNo, UserId = s.CashierId, Notes = s.CustomerName });
            }

        db.StockMovements.AddRange(movements);
        foreach (var sup in db.Suppliers) sup.LastDeliveryAt = openDate;
        db.AuditLogs.Add(new AuditLog { UserId = admin.Id, UserName = admin.FullName, Action = "System", Details = "Database created with sample data" });
        db.SaveChanges();
    }
}
