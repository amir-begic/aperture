using Funk.Web.Data.Entities;

namespace Funk.Web.Features.Attachments;

/// <summary>What a <see cref="FileField"/> edit amounts to: a stored new file and/or one to delete.</summary>
public readonly record struct AttachmentChange(Attachment? Added, Attachment? Removed);
