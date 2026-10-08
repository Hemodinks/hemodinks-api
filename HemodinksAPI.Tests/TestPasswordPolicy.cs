using HemodinksAPI.Application.Security;
using HemodinksAPI.Infrastructure.Security;

namespace HemodinksAPI.Tests;

internal static class TestPasswordPolicy
{
    public static NewPasswordPolicy Instance { get; } = new(new LocalCompromisedPasswordLookup());
}
