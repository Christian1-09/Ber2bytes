# Ber2Bytes — Automated Inventory and Sales Tracking System

Offline Windows desktop app for **Ber2Bytes Computer Parts & Accessories (Dipolog City Branch)**.
Built with **.NET 10 · Blazor Hybrid on WPF · SQLite (EF Core)**.

---

## 1. What you need

| | |
|---|---|
| Windows | 10 or 11 (64-bit) |
| Visual Studio | **Visual Studio 2026** (or newer) with the workloads **.NET desktop development** and **ASP.NET and web development** |
| .NET SDK | **.NET 10** (installed with Visual Studio) |
| WebView2 Runtime | Already included in Windows 11 / updated Windows 10. If the window stays blank, install "Microsoft Edge WebView2 Runtime". |

The first build needs internet **once** so Visual Studio can download the NuGet packages. After that, the app runs fully offline.

## 2. Run it

1. Unzip the folder and open **`Ber2Bytes.sln`**.
2. Wait for "Restore completed" (bottom-left status bar). If it doesn't start, right-click the solution → **Restore NuGet Packages**.
3. Press **F5**.
4. Sign in:

| Role | Username | Password |
|---|---|---|
| Administrator | `admin` | `admin123` |
| Staff | `staff` | `staff123` |

> **Change both passwords** after the first sign-in (top-right user menu → *Change my password*).

The first run creates the database with sample products and about 6 months of **sample sales**, so the dashboard, charts and reports have data. Before going live: **Settings → Start fresh** removes all sample transactions (products, users and settings are kept).
To start with **no** sample sales at all, set `SeedDemoSales = false` in `Data/DatabaseInitializer.cs` before the first run.

## 3. Add your logo and login picture

Put these files in **`src/Ber2Bytes/wwwroot/images/`**, then rebuild:

| File | Used for |
|---|---|
| `logo.png` | Logo on the white side of the login (transparent PNG, ~600 px wide) |
| `logo-dark.png` | *(optional)* Logo version for black backgrounds: sidebar and left side of the login |
| `login-hero.jpg` | Picture on the left side of the login (PC parts photo, ~1200×1400 px) |
| `app.ico` | *(optional)* Window icon. Then uncomment `<ApplicationIcon>` in `Ber2Bytes.csproj` |

Until you add them, a text version of the Ber2Bytes logo and a dark red/black background are shown.

## 4. Features

**Both roles**
- **Point of Sale**: barcode scanner/SKU search, product photos, category filter, cart, discount (% or ₱), **Cash or GCash** (with reference no.), quick-cash buttons, change, **hold/recall** sales, printable receipt. Shortcuts: **F2** scan, **F4** cash/ref box, **F9** charge, **Esc** close dialog, **Ctrl+K** global search.
- Dashboard: sales today vs. yesterday, daily / weekly / monthly charts, sales by category, recent sales, low stock, best sellers, slow-moving items.
- Products (view), Inventory with **Stock In** (receive deliveries), Suppliers (view), Returns, Alerts.
- **Low Stock / Reorder List** per supplier, exported as **PDF** (to print or send) or **Excel**.

**Administrator only**
- Add / edit / archive products with photos, cost price and profit margin.
- Stock adjustments (damaged, lost, physical count).
- All sales, **void** with reason (stock goes back automatically).
- Approve or reject returns (refund or replacement; resellable items go back to stock).
- Reports: **Sales**, **Top-Selling Products**, **Slow-Moving & Dead Stock**, **Inventory Valuation**, each as PDF or Excel.
- Categories, suppliers, **user accounts**, **activity log**, **settings**, **backup / restore**.

**Staff limits**: no cost prices or profit, no deleting, no voids, no stock adjustments, sees only their own sales, discount capped (default 10%, set in Settings), returns need admin approval.

### Slow-Moving & Dead Stock report
Looks at products that still have stock:
- **Dead stock**: no sales at all in the last *N* days (30 / 60 / 90 / 180 / 365, default 60 in Settings).
- **Slow-moving**: sold only a few units (≤ 2 by default) in that period.

It shows days idle, last sold date and the **money tied up** (stock × cost), with tips (sale, bundle, ask the supplier about returns).

### Removed from the prototype (as requested)
VAT · Card payment · Serial numbers & warranty tracking.

## 5. Where the data is

`%LocalAppData%\Ber2Bytes\` (for example, `C:\Users\<you>\AppData\Local\Ber2Bytes\`)

| | |
|---|---|
| `ber2bytes.db` | SQLite database |
| `images\` | Product photos (resized to 500 px JPG) |
| `auto-backups\` | Daily automatic backups (last 14 kept) |
| `error-log.txt` | Error details if something goes wrong |
| `prefs.json` | "Remember me" and sidebar setting for this PC |

**Backups:** Settings → *Create Backup…* makes a `.zip` with the database and photos. Keep a copy on a USB drive or cloud folder. *Restore Backup…* replaces everything and restarts the app (a safety copy is made first).

## 6. Project structure

```
src/Ber2Bytes/
├─ App.xaml(.cs)            Startup: services (DI), database creation, error handling
├─ MainWindow.xaml(.cs)     WPF window hosting the BlazorWebView
├─ Data/
│  ├─ Entities/Entities.cs  Tables: User, Category, Supplier, Product, Sale, SaleItem,
│  │                        StockMovement, ReturnRequest, AuditLog, StoreSettings
│  ├─ AppDbContext.cs       EF Core + SQLite setup
│  └─ DatabaseInitializer.cs First-run data (accounts, categories, products, sample sales)
├─ Services/                Business logic (one class per area)
│  ├─ AuthService.cs        Login, lockout after 5 failed tries, roles & permissions (Perm)
│  ├─ CatalogService.cs     Products, categories, suppliers
│  ├─ InventoryService.cs   Stock in, adjustments, movement history
│  ├─ SalesService.cs       POS cart, checkout (one DB transaction), void
│  ├─ ReturnService.cs      Returns & approvals
│  ├─ ReportService.cs      All reports (incl. dead stock)
│  ├─ ExportService.cs      PDF (QuestPDF) and Excel (ClosedXML)
│  ├─ DashboardService.cs   Dashboard numbers/charts + AlertService
│  ├─ UserService.cs, AuditService.cs, SettingsService.cs
│  └─ FileServices.cs       Photos, Save/Open dialogs, backup/restore
├─ Components/
│  ├─ Layout/               MainLayout (sidebar + top bar), EmptyLayout (login)
│  ├─ Pages/                Login, Dashboard, Pos, Products, Inventory, Sales, Categories,
│  │                        Suppliers, Returns, Reports, Alerts, Users, ActivityLog, SettingsPage
│  └─ Shared/               Modal, charts (SVG), badges, StockInDialog, AppPageBase (role check)
└─ wwwroot/                 index.html (icons), css/app.css (theme), js/app.js, fonts, images
```

**Change the colours:** edit the variables at the top of `wwwroot/css/app.css` (`--red`, `--black`, …).

**Permissions:** every action checks a permission from `Perm` in `Services/AuthService.cs`. To give staff more access, add the permission to `StaffPermissions`.

## 7. Changing the database tables later

The app uses `EnsureCreated()` (simple, no tools needed). If you add or rename properties in `Entities.cs`:
- **While developing:** make a backup if needed, delete `%LocalAppData%\Ber2Bytes\ber2bytes.db` and run again.
- **After going live:** switch to EF Core Migrations (`dotnet ef migrations add …`) so existing data is kept.

## 8. Install on the store computer

```
dotnet publish src/Ber2Bytes -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
```
Copy the `publish` folder to the store PC and create a desktop shortcut to `Ber2Bytes.exe`. No .NET install is needed on that PC.

## 9. Licenses

QuestPDF is used under its free **Community license** (for businesses under USD 1M annual revenue). Montserrat font: SIL Open Font License (`wwwroot/fonts`).
