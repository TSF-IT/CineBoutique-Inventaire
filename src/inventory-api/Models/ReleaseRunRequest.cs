using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ReleaseRunRequest(Guid RunId, Guid OwnerUserId);
