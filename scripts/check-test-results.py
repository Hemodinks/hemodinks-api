"""Fail CI for missing results, failures or silent skips in the selected suite."""
import argparse
from collections import Counter
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SQL_REQUIRED = {
    "HemodinksAPI.Tests.PatientUserTenantSqlServerTests.SqlServer_RejectsCrossClinicPatientAndUserRelationships",
    "HemodinksAPI.Tests.SurgeryTypeSqlServerTests.SurgeryType_MigrationBackfillsLegacyRowsAndAllowsOldWritersWithoutChangingExistingData",
    "HemodinksAPI.Tests.SurgeryTypeSqlServerTests.SurgeryType_RollbackRefusesUrgencyInEitherTableBeforeChangingSchemaOrData",
    "HemodinksAPI.Tests.EventCreatorMigrationTests.CreatorMigration_PreservesLegacyUnknownCreatorAndRejectsCrossClinicAtDatabaseBoundary",
    "HemodinksAPI.Tests.PacienteCommandHandlerTests.DeletePaciente_SqlServer_EnforcesScopeAndCascadesOnlyAuthorizedPatient",
}
BROWSER_REQUIRED = {
    f'HemodinksAPI.Tests.LoginBrowserTests.Browser_UsesRealApi(scenario: "{scenario}")'
    for scenario in ("individual", "selection", "pin", "invalid-pin", "anonymous",
                     "anonymous-write", "cross-tenant", "operator-swap", "replay", "cancel",
                     "layout", "bootstrap-lock")
}


def check(path, suite):
    results = ET.parse(path).getroot().findall(".//t:UnitTestResult", NS)
    if not results:
        raise ValueError("TRX contains no executed tests")
    if any(result.get("outcome") != "Passed" for result in results):
        raise ValueError("TRX contains failed, skipped or incomplete tests")
    names = [result.get("testName") for result in results]
    ids = [result.get("testId") for result in results]
    if any(not test_id for test_id in ids) or len(set(ids)) != len(ids):
        raise ValueError("TRX contains missing or duplicate test IDs")
    required = BROWSER_REQUIRED if suite == "login-e2e" else SQL_REQUIRED
    if not required.issubset(names):
        raise ValueError("TRX is missing required scenarios for this suite")
    if any(Counter(names)[name] != 1 for name in required):
        raise ValueError("TRX contains duplicate required scenarios")
    if suite == "login-e2e" and (set(names) != required or len(names) != len(required)):
        raise ValueError("Login E2E must execute exactly the 12 API browser scenarios")
    if suite == "api" and any(".LoginBrowserTests." in name for name in names):
        raise ValueError("Run browser scenarios through the separate login E2E gate")
    print(f"{suite}: {len(results)} passed, zero skipped; required scenarios present")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--suite", required=True, choices=("api", "login-e2e"))
    parser.add_argument("trx", type=Path)
    args = parser.parse_args()
    try:
        check(args.trx, args.suite)
    except (OSError, ET.ParseError, ValueError) as error:
        # Do not print test stdout, connection strings or arbitrary TRX payloads.
        print(f"Test results gate failed: {type(error).__name__}: {error if isinstance(error, ValueError) else 'missing or invalid TRX'}", file=sys.stderr)
        sys.exit(1)
