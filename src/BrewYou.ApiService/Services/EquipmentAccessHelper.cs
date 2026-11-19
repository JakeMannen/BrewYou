using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services;

internal static class EquipmentAccessHelper
{
    internal static bool IsMember(Equipment equipment, string userId)
    {
        if (equipment.BrewerySetup != null)
        {
            return equipment.BrewerySetup.UserId == userId ||
                   equipment.BrewerySetup.Members.Any(m => m.UserId == userId);
        }

        return equipment.UserId == userId;
    }

    internal static bool IsOwner(Equipment equipment, string userId)
    {
        if (equipment.BrewerySetup != null)
        {
            return equipment.BrewerySetup.UserId == userId ||
                   equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner);
        }

        return equipment.UserId == userId;
    }

    internal static bool IsBrewer(Equipment equipment, string userId)
    {
        return equipment.BrewerySetup != null &&
               equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);
    }

    internal static bool CanDelete(Equipment equipment, string userId)
    {
        return IsOwner(equipment, userId) || (IsBrewer(equipment, userId) && equipment.UserId == userId);
    }

    internal static bool IsSetupOwner(BrewerySetup setup, string userId)
    {
        return setup.UserId == userId ||
               setup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner);
    }

    internal static bool GatewayBelongsToSetupOwner(BrewLinkGateway gateway, BrewerySetup? setup, string fallbackUserId)
    {
        if (setup != null)
        {
            return gateway.UserId == setup.UserId ||
                   setup.Members.Any(m => m.UserId == gateway.UserId && m.Role == BreweryRole.Owner);
        }

        return gateway.UserId == fallbackUserId;
    }

    internal static IReadOnlyCollection<string> GetAuthorizedSetupUserIds(Equipment equipment)
    {
        if (equipment.BrewerySetup == null)
        {
            return string.IsNullOrEmpty(equipment.UserId) ? [] : [equipment.UserId];
        }

        var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(equipment.BrewerySetup.UserId))
        {
            userIds.Add(equipment.BrewerySetup.UserId);
        }

        if (equipment.BrewerySetup.Members != null)
        {
            foreach (var member in equipment.BrewerySetup.Members)
            {
                if (!string.IsNullOrEmpty(member.UserId))
                {
                    userIds.Add(member.UserId);
                }
            }
        }

        return userIds;
    }
}