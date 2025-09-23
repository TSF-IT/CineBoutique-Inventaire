using System;
using System.Data;
using System.Runtime.CompilerServices;
using Dapper;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

/// <summary>
/// TypeHandler Dapper pour convertir les DateTime retournés par Npgsql
/// en DateTimeOffset (UTC).
/// </summary>
internal sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        // Laisser Dapper/Npgsql gérer l'écriture en DateTime (UTC)
        parameter.Value = value.UtcDateTime;
        parameter.DbType = DbType.DateTime;
    }

    public override DateTimeOffset Parse(object value)
    {
        // Dapper peut donner un DateTime, une string, ou déjà un DateTimeOffset
        switch (value)
        {
            case DateTimeOffset dto:
                return dto;

            case DateTime dt:
                // Si pas de Kind, on considère UTC pour stabilité CI
                if (dt.Kind == DateTimeKind.Unspecified)
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero);

            case string s when DateTime.TryParse(s, out var parsed):
                if (parsed.Kind == DateTimeKind.Unspecified)
                    parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                return new DateTimeOffset(parsed.ToUniversalTime(), TimeSpan.Zero);

            default:
                // Laisser Dapper râler avec un message utile
                throw new InvalidCastException($"Cannot convert value of type '{value?.GetType().FullName ?? "null"}' to DateTimeOffset.");
        }
    }
}

internal static class DapperBootstrap
{
    // S’exécute au chargement de l’assembly tests
    [ModuleInitializer]
    public static void Initialize()
    {
        // Enregistre la TypeHandler une seule fois
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }
}
