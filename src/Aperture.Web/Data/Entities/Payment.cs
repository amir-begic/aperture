using System.ComponentModel.DataAnnotations;
using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

/// <summary>Money received for a shoot.</summary>
public class Payment
{
    public int Id { get; set; }

    public int ShootId { get; set; }
    public Shoot Shoot { get; set; } = null!;

    public DateOnly Date { get; set; }

    public Money Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;

    /// <summary>E.g. "Deposit" or the bank reference.</summary>
    [MaxLength(150)]
    public string? Note { get; set; }

    /// <summary>Payslip, bank screenshot or cash receipt.</summary>
    public int? ProofId { get; set; }
    public Attachment? Proof { get; set; }
}
