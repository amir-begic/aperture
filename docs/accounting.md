# How Aperture calculates

How the figures in Aperture come about, so you can check them and explain them to a fiduciary or the tax office.
Aperture keeps simplified records for a self-employed sole trader in Switzerland. It doesn't give tax advice;
when in doubt, ask your fiduciary or the cantonal tax office.

All amounts are in CHF, calculated to the Rappen.

## Gigs

| Status | Meaning |
|---|---|
| **Inquiry** | A client asked; the date is held. Time and venue may still be unknown. |
| **Confirmed** | Booked. |
| **Done** | Played. |
| **Cancelled** | Kept for the records; it no longer blocks the date. |

### Money per gig

- **Quote:** the agreed fee.
- **Costs:** what the gig cost you, e.g. renting lights. Each cost is either
  - **covered by the quote:** it reduces your margin and doesn't appear on the invoice, or
  - **billed to the client:** it appears as its own line on the invoice, at what you paid or at a higher price
    with your margin on top.
- **Total:** quote plus billed costs. Once an invoice is issued, its total.
- **Margin:** total minus all costs.
- **Received:** the payments recorded for the gig, each with an optional payslip.
- **Open:** total minus received, never below zero.

The payment state follows from this: *unpaid*, *partly paid*, *paid*, or *no quote* while there's nothing to bill yet.

## Invoices

1. **Draft:** created from the gig with the quote and the billed costs as lines. You can change lines, dates
   and amounts. A draft has no number yet and can be deleted.
2. **Issued:** gets the next number of its year (`2026-001`, `2026-002`, …). From then on it can't change:
   your and the client's addresses are copied into it, and the PDF is archived with the gig's files.
3. **Cancelled:** keeps its number, so the sequence has no gaps. Create a new invoice to replace it.

The total is calculated as:

```
  sum of lines (each line rounded to the Rappen)
+ VAT, only if you're registered for VAT (Settings, default rate 8.1%)
± rounding to 5 Rappen
= total
− already paid (payments recorded for the gig when the invoice is issued, e.g. a deposit)
= amount due, which is also the amount on the QR-bill
```

The **QR-bill** uses an RF creditor reference made from the invoice number. Your bank reports it with the payment,
so you can see which invoice was paid. Payments still have to be entered in Aperture by hand.

An invoice is **overdue** when its due date has passed and the gig isn't paid in full. The due date defaults to
the payment terms in Settings (30 days).

## Income statement

**Finance** shows one year on a **cash basis**, the method of simplified bookkeeping for small businesses:

| | Counted in the year when… |
|---|---|
| **Income** | the money arrived (the payment date), not when the gig was played or invoiced. |
| **Costs** | you paid them. These are gig costs plus business costs that don't belong to a gig (DJ pools, software, phone, …). |
| **Depreciation** | see below. |

```
  income
− costs, by category
− depreciation of equipment
± result from selling equipment (sale price minus remaining book value)
= profit or loss
```

A gig played in December and paid in January counts as income in January's year.

## Equipment and depreciation

Equipment that lasts several years (controllers, speakers, laptop) isn't a cost in the year you buy it.
It's depreciated: each year a share of its remaining value counts as a cost.

Aperture uses the **declining-balance method** (*degressiv, vom Buchwert*): the rate applies to the remaining book value
each year. The default rates are the maximums from the ESTV's *Merkblatt A 1995*, which the canton of St. Gallen
also applies (StB 41 Nr. 1):

| Category | Rate | ESTV class |
|---|---|---|
| DJ & audio equipment | 30% | Apparate und Maschinen |
| Lighting & effects | 30% | Apparate und Maschinen |
| Computer & software | 40% | Datenverarbeitungsanlagen |
| Vehicle | 40% | Motorfahrzeuge |
| Furniture & cases | 25% | Geschäftsmobiliar |
| Other | 30% | Apparate und Maschinen |

Each item keeps its own rate; a lower rate is always allowed.

- **Year of purchase:** the full rate, as St. Gallen practice allows (*nicht pro rata temporis gekürzt*).
  If your canton or fiduciary wants it by month, switch off *Full annual depreciation in the year of purchase*
  under Settings.
  The month of purchase then counts as a full month.
- **Year of sale:** by month, up to the month of sale (that month doesn't count). The difference between the
  sale price and the remaining book value is a gain or a loss.
- **Bought and sold in the same year:** only the months actually held.
- **Small remainders:** below CHF 1, the rest is written off.

Example: speakers for CHF 2,000 at 30%, bought in March 2026:

| Year | Book value at start | Depreciation | Book value at end |
|---|---|---|---|
| 2026 | 2,000.00 | 600.00 | 1,400.00 |
| 2027 | 1,400.00 | 420.00 | 980.00 |
| 2028 | 980.00 | 294.00 | 686.00 |

## Tax export

**Finance → Tax export** creates, per year, one ZIP with everything, or each part on its own:

- **Statement (PDF):** the income statement as above, with costs by category, the equipment list and every line.
- **Accounts (Excel):** the same lines, one sheet each for the summary, income, costs and equipment, for a fiduciary
  or your own checks.
- **Documents (in the ZIP):** every receipt, payslip and invoice of the year. Each gets a number (`R001`, `R002`, …) that also
  appears next to its line in the PDF and in Excel, so each line can be matched to its document.

Swiss law requires business records to be kept for 10 years. The nightly backup keeps one database copy per month
for good, and never deletes receipts.

## VAT

Registration for VAT (MWST) is mandatory from CHF 100,000 of yearly turnover. Until then, leave it off in Settings
and invoices show no VAT. Once registered, enter your VAT number and rate: invoices then show VAT and your number.
Aperture doesn't create VAT returns.
