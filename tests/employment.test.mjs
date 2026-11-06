import test from "node:test";
import assert from "node:assert/strict";
import {
  parseEmploymentDetails,
  newEmploymentDetails,
} from "../app/lib/employment.ts";

const joined = "2024-01-15";

test("omitted fields keep their current value and blanks clear them", () => {
  const current = {
    ...newEmploymentDetails,
    position: "Analyst",
    employee_level: 2,
    department_id: 3,
  };
  assert.deepEqual(parseEmploymentDetails({}, joined, current), current);
  assert.deepEqual(
    parseEmploymentDetails(
      { position: "", employee_level: null, department_id: "" },
      joined,
      current,
    ),
    newEmploymentDetails,
  );
});

test("form values are parsed into typed employment details", () => {
  assert.deepEqual(
    parseEmploymentDetails(
      {
        position: "  Consultant  ",
        employment_type: "Full-time",
        employee_level: "3",
        probation_completed_on: "2024-04-15",
        leaving_date: "2027-01-31",
        is_active: false,
        department_id: "4",
        cost_centre_id: 7,
      },
      joined,
      newEmploymentDetails,
    ),
    {
      position: "Consultant",
      employment_type: "Full-time",
      employee_level: 3,
      probation_completed_on: "2024-04-15",
      leaving_date: "2027-01-31",
      is_active: false,
      department_id: 4,
      cost_centre_id: 7,
    },
  );
});

test("unsupported values are rejected rather than defaulted", () => {
  for (const input of [
    { position: "x".repeat(101) },
    { employment_type: "Contract" },
    { employee_level: 0 },
    { employee_level: "5" },
    { employee_level: 1.5 },
    { probation_completed_on: "2024-02-30" },
    { leaving_date: "2023-12-31" },
    { is_active: "yes" },
    { department_id: -1 },
    { cost_centre_id: "abc" },
  ])
    assert.equal(
      typeof parseEmploymentDetails(input, joined, newEmploymentDetails),
      "string",
      JSON.stringify(input),
    );
});

test("a leaving date is checked against the new join date", () => {
  const current = { ...newEmploymentDetails, leaving_date: "2025-06-30" };
  assert.equal(
    typeof parseEmploymentDetails({}, "2025-07-01", current),
    "string",
  );
  assert.equal(
    parseEmploymentDetails({}, "2025-06-30", current).leaving_date,
    "2025-06-30",
  );
});
