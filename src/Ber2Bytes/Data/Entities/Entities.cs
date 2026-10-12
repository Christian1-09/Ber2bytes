using System.ComponentModel.DataAnnotations;

namespace Ber2Bytes.Data.Entities;

// ─────────────────────────────── Enums ───────────────────────────────
public enum UserRole { Admin = 1, Staff = 2 }
public enum PaymentMethod { Cash = 1, GCash = 2 }
public enum SaleStatus { Completed = 1, Voided = 2 }
public enum MovementType { StockIn = 1, Sale = 2, Adjustment = 3, Return = 4, Replacement = 5, Void = 6 }
public enum ReturnStatus { Pending = 1, Completed = 2, Rejected = 3 }
public enum ItemCondition { Resellable = 1, Defective = 2 }
public enum ReturnResolution { Refund = 1, Replacement = 2 }

// ─────────────────────────────── Tables ──────────────────────────────
public class User
{
    public int Id { get; set; }
    [MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(50)] public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Staff;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }
}

public class Category
{
    public int Id { get; set; }
    [MaxLength(80)] public string Name { get; set; } = "";
    [MaxLength(250)] public string? Description { get; set; }
    [MaxLength(30)] public string Icon { get; set; } = "box";
    [MaxLength(10)] public string Color { get; set; } = "#E11D2A";
    public List<Product> Products { get; set; } = [];
}

public class Supplier
{
    public int Id { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? ContactPerson { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(120)] public string? Email { get; set; }
    [MaxLength(250)] public string? Address { get; set; }
    [MaxLength(30)] public string PaymentTerms { get; set; } = "COD";
    public DateTime? LastDeliveryAt { get; set; }
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    [MaxLength(40)] public string Sku { get; set; } = "";
    [MaxLength(40)] public string? Barcode { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(60)] public string? Brand { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public int Stock { get; set; }
    public int ReorderLevel { get; set; } = 5;
    [MaxLength(1000)] public string? Description { get; set; }
    /// <summary>File name inside the images folder (null = no photo).</summary>
    [MaxLength(80)] public string? ImageFile { get; set; }
    /// <summary>Archived products are hidden from the POS but keep their sales history.</summary>
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public bool IsLowStock => Stock <= ReorderLevel;
    public bool IsOutOfStock => Stock <= 0;
}

public class Sale
{
    public int Id { get; set; }
    [MaxLength(20)] public string InvoiceNo { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    [MaxLength(100)] public string CustomerName { get; set; } = "Walk-in";
    [MaxLength(40)] public string? CustomerPhone { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    [MaxLength(60)] public string? ReferenceNo { get; set; }
    public decimal AmountTendered { get; set; }
    public decimal Change { get; set; }
    public int CashierId { get; set; }
    public User? Cashier { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    [MaxLength(250)] public string? VoidReason { get; set; }
    public int? VoidedById { get; set; }
    public DateTime? VoidedAt { get; set; }
    public List<SaleItem> Items { get; set; } = [];
}

public class SaleItem
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    /// <summary>Copied at the time of sale so receipts never change.</summary>
    [MaxLength(200)] public string ProductName { get; set; } = "";
    [MaxLength(40)] public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>How many units of this line were already returned.</summary>
    public int ReturnedQty { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class StockMovement
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public MovementType Type { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    /// <summary>Positive = added to stock, negative = removed.</summary>
    public int Quantity { get; set; }
    public int StockAfter { get; set; }
    [MaxLength(40)] public string? Reference { get; set; }
    public int? SupplierId { get; set; }
    public decimal? UnitCost { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    [MaxLength(250)] public string? Notes { get; set; }
}

public class ReturnRequest
{
    public int Id { get; set; }
    [MaxLength(20)] public string ReturnNo { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }
    public int SaleItemId { get; set; }
    public SaleItem? SaleItem { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; } = 1;
    [MaxLength(500)] public string Reason { get; set; } = "";
    public ItemCondition Condition { get; set; } = ItemCondition.Defective;
    public ReturnResolution Resolution { get; set; } = ReturnResolution.Replacement;
    public ReturnStatus Status { get; set; } = ReturnStatus.Pending;
    public decimal RefundAmount { get; set; }
    public int CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public int? ProcessedById { get; set; }
    public User? ProcessedBy { get; set; }
    public DateTime? ProcessedAt { get; set; }
    [MaxLength(250)] public string? AdminNote { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public int? UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = "";
    [MaxLength(60)] public string Action { get; set; } = "";
    [MaxLength(500)] public string Details { get; set; } = "";
}

/// <summary>Single row (Id = 1) holding the store settings.</summary>
public class StoreSettings
{
    public int Id { get; set; } = 1;
    [MaxLength(120)] public string StoreName { get; set; } = "Ber2Bytes Computer Parts & Accessories";
    [MaxLength(80)] public string BranchName { get; set; } = "Dipolog City Branch";
    [MaxLength(250)] public string? Address { get; set; } = "Dipolog City, Zamboanga del Norte";
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(300)] public string ReceiptFooter { get; set; } = "Thank you for shopping at Ber2Bytes! Please keep this receipt for returns.";
    public int DefaultReorderLevel { get; set; } = 5;
    public decimal StaffMaxDiscountPercent { get; set; } = 10;
    /// <summary>A product with stock but no sales in this many days is "dead stock".</summary>
    public int DeadStockDays { get; set; } = 60;
    /// <summary>Selling this many units or fewer in the period counts as "slow-moving".</summary>
    public int SlowMovingMaxQty { get; set; } = 2;
    /// <summary>Returns are accepted within this many days of purchase.</summary>
    public int ReturnWindowDays { get; set; } = 7;
    public bool AutoBackupOnExit { get; set; } = true;
}
