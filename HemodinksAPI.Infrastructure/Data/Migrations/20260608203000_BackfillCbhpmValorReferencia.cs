using HemodinksAPI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260608203000_BackfillCbhpmValorReferencia")]
    public partial class BackfillCbhpmValorReferencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE CBHPMGeral
                SET ValorReferencia = CAST(ROUND(
                    CASE UPPER(LTRIM(RTRIM(Porte)))
                        WHEN '1A' THEN 12.86
                        WHEN '1B' THEN 25.72
                        WHEN '1C' THEN 38.58
                        WHEN '2A' THEN 51.45
                        WHEN '2B' THEN 67.82
                        WHEN '2C' THEN 80.26
                        WHEN '3A' THEN 109.67
                        WHEN '3B' THEN 140.14
                        WHEN '3C' THEN 160.52
                        WHEN '4A' THEN 191.04
                        WHEN '4B' THEN 209.13
                        WHEN '4C' THEN 236.26
                        WHEN '5A' THEN 254.34
                        WHEN '5B' THEN 274.69
                        WHEN '5C' THEN 291.64
                        WHEN '6A' THEN 317.65
                        WHEN '6B' THEN 349.30
                        WHEN '6C' THEN 382.08
                        WHEN '7A' THEN 412.60
                        WHEN '7B' THEN 456.68
                        WHEN '7C' THEN 540.33
                        WHEN '8A' THEN 583.29
                        WHEN '8B' THEN 611.55
                        WHEN '8C' THEN 648.85
                        WHEN '9A' THEN 689.55
                        WHEN '9B' THEN 753.99
                        WHEN '9C' THEN 830.84
                        WHEN '10A' THEN 891.89
                        WHEN '10B' THEN 966.50
                        WHEN '10C' THEN 1072.75
                        WHEN '11A' THEN 1134.93
                        WHEN '11B' THEN 1244.58
                        WHEN '11C' THEN 1365.54
                        WHEN '12A' THEN 1415.27
                        WHEN '12B' THEN 1521.53
                        WHEN '12C' THEN 1864.04
                        WHEN '13A' THEN 2051.69
                        WHEN '13B' THEN 2250.64
                        WHEN '13C' THEN 2489.16
                        WHEN '14A' THEN 2774.02
                        WHEN '14B' THEN 3018.19
                        WHEN '14C' THEN 3329.05
                    END + (COALESCE(CustoOperacional, 0) * 14.33),
                    2) AS decimal(18,2))
                WHERE ValorReferencia IS NULL
                    AND UPPER(LTRIM(RTRIM(Porte))) IN (
                        '1A', '1B', '1C',
                        '2A', '2B', '2C',
                        '3A', '3B', '3C',
                        '4A', '4B', '4C',
                        '5A', '5B', '5C',
                        '6A', '6B', '6C',
                        '7A', '7B', '7C',
                        '8A', '8B', '8C',
                        '9A', '9B', '9C',
                        '10A', '10B', '10C',
                        '11A', '11B', '11C',
                        '12A', '12B', '12C',
                        '13A', '13B', '13C',
                        '14A', '14B', '14C');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE CBHPMGeral
                SET ValorReferencia = NULL
                WHERE UPPER(LTRIM(RTRIM(Porte))) IN (
                    '1A', '1B', '1C',
                    '2A', '2B', '2C',
                    '3A', '3B', '3C',
                    '4A', '4B', '4C',
                    '5A', '5B', '5C',
                    '6A', '6B', '6C',
                    '7A', '7B', '7C',
                    '8A', '8B', '8C',
                    '9A', '9B', '9C',
                    '10A', '10B', '10C',
                    '11A', '11B', '11C',
                    '12A', '12B', '12C',
                    '13A', '13B', '13C',
                    '14A', '14B', '14C');
                """);
        }
    }
}
