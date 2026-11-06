"use client";

import { FormEvent, useState, useEffect } from "react";
import Link from "next/link";
import {
  annualAllowance,
  sickAllowance,
  datesBetween,
  isWeekday,
  validateLeave,
  type LeaveEntry,
} from "./lib/time";

type Employee = {
  id: number;
  name: string;
  email: string;
  join_date: string;
  role: string;
};

type Request = {
  employee_id: number;
  id: number;
  person: string;
  kind: "Leave" | "Claim";
  category: string;
  detail: string;
  value: number;
  status: "Pending" | "Approved" | "Rejected";
  note: string;
  entries?: LeaveEntry[];
};
const money = (value: number) => `RM ${value.toFixed(2)}`;
const dateLabel = (value: string) =>
  new Date(`${value}T00:00:00`).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });

function Icon({ name }: { name: string }) {
  const paths: Record<string, string> = {
    dashboard: "M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z",
    leave:
      "M5 5h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M16 3v4 M8 3v4 M3 11h18",
    claim: "M6 3h12v18l-3-2-3 2-3-2-3 2z M9 8h6 M9 12h6",
    approvals: "M9 12l2 2 4-4 M12 3l9 4v5c0 5-9 9-9 9s-9-4-9-9V7z",
    arrow: "M5 12h14 M14 7l5 5-5 5",
    plus: "M12 5v14 M5 12h14",
  };
  return (
    <svg
      width="20"
      height="20"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name] || paths.dashboard} />
    </svg>
  );
}

export default function Home() {
  const [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [connectionError, setConnectionError] = useState("");
  const [dirtyTimesheet, setDirtyTimesheet] = useState(false);
  const [employee, setEmployee] = useState({
    id: 0,
    role: "Employee",
    name: "Amirul",
    join_date: "2024-01-15",
  });
  const [view, setView] = useState("Dashboard");
  const [role, setRole] = useState("Employee");
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [employeeEditor, setEmployeeEditor] =
    useState<Partial<Employee> | null>(null);
  const canManageEmployees = loaded && ["HR", "Admin"].includes(employee.role);
  const [requests, setRequests] = useState<Request[]>([]);
  const [modal, setModal] = useState<"Leave" | "Claim" | null>(null);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [filter, setFilter] = useState("All");
  const [month, setMonth] = useState("2026-10");
  const [worked, setWorked] = useState<Record<string, string>>({});
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [leaveCategory, setLeaveCategory] = useState("Annual leave");
  const [leaveHours, setLeaveHours] = useState<Record<string, string>>({});
  const allowance = annualAllowance(employee.join_date, "2026-10-06"); // Demo employee join date.
  const medicalAllowance = sickAllowance(employee.join_date, "2026-10-06");
  const isMedical =
    leaveCategory === "Medical leave" ||
    leaveCategory === "Hospitalisation leave";
  const leaveDates = datesBetween(startDate, endDate).filter(
    (date) => isMedical || isWeekday(date),
  );
  const monthDates = month
    ? datesBetween(
        `${month}-01`,
        `${month}-${new Date(Number(month.slice(0, 4)), Number(month.slice(5)), 0).getDate()}`,
      )
    : [];
  const mine = requests.filter(
    (request) => request.employee_id === employee.id,
  );
  const pending = requests.filter((request) => request.status === "Pending");
  const balance =
    allowance -
    mine
      .filter(
        (request) =>
          request.kind === "Leave" &&
          request.category === "Annual leave" &&
          request.status === "Approved",
      )
      .reduce((sum, request) => sum + request.value, 0);
  const awaitingClaims = mine
    .filter(
      (request) => request.kind === "Claim" && request.status === "Pending",
    )
    .reduce((sum, request) => sum + request.value, 0);
  const visible = (view === "HR approvals" ? requests : mine).filter(
    (request) =>
      (view === "My leave"
        ? request.kind === "Leave"
        : view === "My claims"
          ? request.kind === "Claim"
          : true) &&
      (filter === "All" || request.status === filter),
  );

  async function refresh() {
    const response = await fetch("/api/workspace", {
      cache: "no-store",
      headers: { "x-demo-role": role },
    });
    const data = await response.json();
    if (!response.ok) throw new Error(data.error);
    setRequests(data.requests);
    setWorked(data.worked);
    setEmployee(data.employee);
    setLoaded(true);
    setConnectionError("");
  }
  useEffect(() => {
    let active = true;
    fetch("/api/workspace", {
      cache: "no-store",
      headers: { "x-demo-role": role },
    })
      .then(async (response) => {
        const data = await response.json();
        if (!response.ok) throw new Error(data.error);
        if (active) {
          setRequests(data.requests);
          setWorked(data.worked);
          setEmployee(data.employee);
          setLoaded(true);
        }
      })
      .catch((error) => {
        if (active) setConnectionError(error.message);
      });
    return () => {
      active = false;
    };
  }, [role]);
  async function persist(method: string, body: object) {
    const response = await fetch("/api/workspace", {
      method,
      headers: { "Content-Type": "application/json", "x-demo-role": role },
      body: JSON.stringify(body),
    });
    const data = await response.json();
    if (!response.ok) throw new Error(data.error);
  }
  async function saveTimesheet() {
    if (busy || !loaded) return;
    setBusy(true);
    try {
      await persist("PUT", {
        month,
        worked: Object.fromEntries(
          Object.entries(worked).filter(([date]) =>
            date.startsWith(month + "-"),
          ),
        ),
      });
      setDirtyTimesheet(false);
      setMessage("Timesheet saved to the database.");
      setConnectionError("");
    } catch (error) {
      setConnectionError(
        error instanceof Error ? error.message : "Could not save timesheet.",
      );
    } finally {
      setBusy(false);
    }
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !loaded) return;
    if (dirtyTimesheet) {
      setError("Save your timesheet before submitting a request.");
      return;
    }
    const data = new FormData(event.currentTarget);
    const category = String(data.get("category"));
    let value = Number(data.get("amount"));
    let detail = String(data.get("description") || "");
    if (modal === "Leave") {
      const entries = leaveDates.map((date) => ({
        date,
        hours: isMedical ? 8 : Number(leaveHours[date] ?? "8"),
      }));
      if (startDate.slice(0, 4) !== endDate.slice(0, 4)) {
        setError("Submit separate requests for each calendar year.");
        return;
      }
      if (startDate.slice(0, 4) !== "2026") {
        setError("This demo has balances for 2026 only.");
        return;
      }
      const existing = mine
        .filter((r) => r.kind === "Leave" && r.status !== "Rejected")
        .flatMap((r) => r.entries ?? []);
      const reserved = mine
        .filter((r) => r.category === category && r.status === "Pending")
        .reduce((sum, r) => sum + r.value * 8, 0);
      const categoryBalance =
        category === "Annual leave"
          ? balance * 8
          : category === "Medical leave" || category === "Hospitalisation leave"
            ? ((category === "Medical leave" ? medicalAllowance : 60) -
                mine
                  .filter(
                    (r) => r.category === category && r.status === "Approved",
                  )
                  .reduce((sum, r) => sum + r.value, 0)) *
              8
            : Infinity;
      const validation = validateLeave(
        entries,
        existing,
        categoryBalance - reserved,
      );
      if (validation) {
        setError(validation);
        return;
      }
      if (
        entries.some(
          (e) =>
            Number(worked[e.date] || 0) +
              e.hours +
              existing
                .filter((other) => other.date === e.date)
                .reduce((sum, other) => sum + other.hours, 0) >
            8,
        )
      ) {
        setError(
          "Worked hours plus leave would exceed 8 hours. Update the timesheet first.",
        );
        return;
      }
      value = entries.reduce((sum, e) => sum + e.hours, 0) / 8;
      detail = `${dateLabel(startDate)} – ${dateLabel(endDate)}`;
    } else if (!Number.isFinite(value) || value <= 0) {
      setError("Enter a claim amount greater than zero.");
      return;
    }
    setBusy(true);
    try {
      await persist("POST", {
        kind: modal,
        category,
        detail,
        value,
        note: String(data.get("note")),
        entries:
          modal === "Leave"
            ? leaveDates.map((date) => ({
                date,
                hours: isMedical ? 8 : Number(leaveHours[date] ?? "8"),
              }))
            : [],
      });
      await refresh();
      setMessage(
        `${modal === "Leave" ? "Leave request" : "Claim"} saved for HR review.`,
      );
      setModal(null);
      setError("");
    } catch (error) {
      setError(
        error instanceof Error ? error.message : "Could not save request.",
      );
    } finally {
      setBusy(false);
    }
  }
  async function decide(id: number, status: "Approved" | "Rejected") {
    if (busy || !loaded) return;
    if (dirtyTimesheet) {
      setMessage("Save your timesheet before reviewing requests.");
      return;
    }
    const target = requests.find((r) => r.id === id);
    if (!target || target.status !== "Pending") return;
    const medicalVerified = ["Medical leave", "Hospitalisation leave"].includes(
      target.category,
    )
      ? window.confirm(
          "Have you verified medical certification, notification, and statutory eligibility? A rejection must have a lawful basis.",
        )
      : false;
    if (
      ["Medical leave", "Hospitalisation leave"].includes(target.category) &&
      !medicalVerified
    )
      return;
    setBusy(true);
    try {
      await persist("PATCH", { id, status, medicalVerified });
      await refresh();
      setMessage(`Request ${status.toLowerCase()} and saved.`);
    } catch (error) {
      setConnectionError(
        error instanceof Error ? error.message : "Could not save decision.",
      );
    } finally {
      setBusy(false);
    }
  }
  async function loadEmployees() {
    const response = await fetch("/api/employees", {
      cache: "no-store",
      headers: { "x-demo-role": role },
    });
    const data = await response.json();
    if (!response.ok) throw new Error(data.error);
    setEmployees(data.employees);
  }
  async function saveEmployee(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !loaded) return;
    const form = new FormData(event.currentTarget);
    setBusy(true);
    setError("");
    try {
      const response = await fetch("/api/employees", {
        method: employeeEditor?.id ? "PATCH" : "POST",
        headers: { "Content-Type": "application/json", "x-demo-role": role },
        body: JSON.stringify({
          id: employeeEditor?.id,
          name: form.get("name"),
          email: form.get("email"),
          join_date: form.get("join_date"),
          role: form.get("role") ?? "Employee",
        }),
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error);
      await loadEmployees();
      await refresh();
      setEmployeeEditor(null);
      setMessage("Employee profile saved.");
    } catch (error) {
      setError(
        error instanceof Error ? error.message : "Could not save employee.",
      );
    } finally {
      setBusy(false);
    }
  }
  function navigate(next: string) {
    if (busy) return;
    if (next === "Employees") {
      setEmployees([]);
      void loadEmployees().catch((error) => setConnectionError(error.message));
    }
    setView(next);
    setFilter("All");
  }
  function openForm(kind: "Leave" | "Claim") {
    setError("");
    setStartDate("");
    setEndDate("");
    setLeaveHours({});
    setLeaveCategory("Annual leave");
    setModal(kind);
  }

  return (
    <div className="workspace">
      <aside className="sidebar">
        <Link className="brand" href="/" aria-label="People and Co home">
          <span className="brand-mark">
            p<span>•</span>
          </span>
          <span>
            people<span className="brand-light"> & co.</span>
          </span>
        </Link>
        <div className="workspace-label">YOUR WORKSPACE</div>
        <nav aria-label="Main navigation">
          {[
            ["Dashboard", "dashboard"],
            ["My leave", "leave"],
            ["My claims", "claim"],
            ["My timesheet", "leave"],
            ...(canManageEmployees
              ? [
                  ["HR approvals", "approvals"],
                  ["Employees", "dashboard"],
                ]
              : []),
          ].map(([label, icon]) => (
            <button
              className={`nav-item ${view === label ? "active" : ""}`}
              key={label}
              onClick={() => navigate(label)}
            >
              <Icon name={icon} />
              {label}
              {label === "HR approvals" && (
                <span className="nav-count">{pending.length}</span>
              )}
            </button>
          ))}
        </nav>
        <div className="sidebar-note">
          <span className="small-leaf">✳</span>
          <h3>A little time for you.</h3>
          <p>Rest, recharge, and come back with a fresh perspective.</p>
          <button onClick={() => openForm("Leave")}>
            Plan your next break <span>↗</span>
          </button>
        </div>
        <div className="profile">
          <div className="avatar">
            {employee.name
              .split(" ")
              .map((part) => part[0])
              .slice(0, 2)
              .join("")}
          </div>
          <div>
            <strong>{employee.name}</strong>
            <span>
              {employee.role === "Admin"
                ? "Admin · Superuser"
                : `${employee.role} workspace`}
            </span>
          </div>
          <span className="online-dot" />
        </div>
      </aside>
      <div className="main-area">
        <header className="topbar">
          <span>
            Workspace <span className="breadcrumb">/ {view}</span>
          </span>
          <div className="topbar-right">
            <span className="demo-tag">Demo workspace</span>
            <label className="role-control">
              <span className="sr-only">Preview role</span>
              <select
                aria-label="Preview role"
                disabled={busy || dirtyTimesheet}
                value={role}
                onChange={(event) => {
                  setLoaded(false);
                  setEmployees([]);
                  setEmployeeEditor(null);
                  setModal(null);
                  setMessage("");
                  setRequests([]);
                  setWorked({});
                  setRole(event.target.value);
                  navigate(
                    event.target.value !== "Employee"
                      ? "HR approvals"
                      : "Dashboard",
                  );
                }}
              >
                <option>Employee</option>
                <option>HR</option>
                <option>Admin</option>
              </select>
            </label>
            <div className="avatar small">
              {employee.name
                .split(" ")
                .map((part) => part[0])
                .slice(0, 2)
                .join("")}
            </div>
          </div>
        </header>
        <main>
          <div className="page-heading">
            <div>
              <div className="eyebrow">TUESDAY, 6 OCTOBER 2026</div>
              <h1>
                {view === "Dashboard"
                  ? `Good morning, ${employee.name}`
                  : view === "Employees"
                    ? "Your people, in one place"
                    : view === "HR approvals"
                      ? "A little review goes a long way"
                      : view === "My leave"
                        ? "Your time, thoughtfully planned"
                        : view === "My timesheet"
                          ? "Every hour, accounted for"
                          : "Your expenses, all in one place"}
                <span className="heading-dot">.</span>
              </h1>
              <p>
                {view === "Employees"
                  ? "Create employee profiles and keep your team records up to date."
                  : view === "HR approvals"
                    ? "Review employee requests. HR reviews requests; Admin has full access."
                    : "A clear view of your time off and expenses. Let’s make today a good one."}
              </p>
            </div>
            <button
              className="button primary"
              disabled={!loaded || busy}
              onClick={() =>
                view === "Employees"
                  ? (setError(""), setEmployeeEditor({ role: "Employee" }))
                  : openForm(view === "My claims" ? "Claim" : "Leave")
              }
            >
              <Icon name="plus" />
              {view === "Employees"
                ? "Add employee"
                : view === "My claims"
                  ? "Submit claim"
                  : "Apply for leave"}
            </button>
          </div>
          {!loaded && !connectionError && (
            <div className="notice" role="status">
              Connecting to your database…
            </div>
          )}
          {connectionError && (
            <div className="notice error-notice" role="alert">
              {connectionError}
              <button
                onClick={() => {
                  refresh().catch((error) => setConnectionError(error.message));
                }}
              >
                Retry
              </button>
            </div>
          )}
          {message && (
            <div className="notice" role="status">
              ✓ {message}
              <button
                aria-label="Dismiss notification"
                onClick={() => setMessage("")}
              >
                ×
              </button>
            </div>
          )}
          {["Dashboard", "My leave", "My claims"].includes(view) && (
            <section
              className={`stats-grid ${view === "My leave" ? "leave-summary" : view === "My claims" ? "claim-summary" : ""}`}
              aria-label="Your summary"
            >
              {view !== "My claims" && (
                <>
                  <div className="stat-card featured">
                    <div className="stat-title">
                      Annual leave{" "}
                      <span>
                        <Icon name="leave" />
                      </span>
                    </div>
                    <div className="stat-number">
                      {balance}
                      <span>days available</span>
                    </div>
                    <div className="balance-track">
                      <span
                        style={{
                          width: `${Math.max(0, (balance / allowance) * 100)}%`,
                        }}
                      />
                    </div>
                    <div className="stat-footer">
                      {allowance - balance} of {allowance} days used{" "}
                      <span>{balance * 8} hours available</span>
                    </div>
                  </div>
                  <div className="stat-card">
                    <div className="stat-title">
                      Medical leave <span className="icon-soft">✚</span>
                    </div>
                    <div className="stat-number">
                      {medicalAllowance -
                        mine
                          .filter(
                            (r) =>
                              r.category === "Medical leave" &&
                              r.status === "Approved",
                          )
                          .reduce((sum, r) => sum + r.value, 0)}
                      <span>days available</span>
                    </div>
                    <p className="stat-subtext">
                      Here when you need to recover.
                    </p>
                    <div className="stat-footer">
                      Annual allowance{" "}
                      <span>
                        {medicalAllowance} days · 60 hospitalisation days
                        separately
                      </span>
                    </div>
                  </div>
                </>
              )}
              {view !== "My leave" && (
                <div className="stat-card">
                  <div className="stat-title">
                    Claims awaiting approval{" "}
                    <span>
                      <Icon name="claim" />
                    </span>
                  </div>
                  <div className="stat-number currency">
                    {money(awaitingClaims)}
                  </div>
                  <p className="stat-subtext">
                    {
                      mine.filter(
                        (r) => r.kind === "Claim" && r.status === "Pending",
                      ).length
                    }{" "}
                    claim(s) with HR for review
                  </p>
                  <button
                    className="text-link"
                    onClick={() => navigate("My claims")}
                  >
                    View your claims <Icon name="arrow" />
                  </button>
                </div>
              )}
            </section>
          )}
          {view === "Dashboard" && (
            <section className="middle-grid">
              <div className="break-card">
                <div>
                  <span className="eyebrow">MAKE ROOM FOR WHAT MATTERS</span>
                  <h2>
                    Your next chapter starts
                    <br />
                    with a little time off.
                  </h2>
                  <p>A well-deserved break is always a good idea.</p>
                  <button
                    className="button light"
                    onClick={() => openForm("Leave")}
                  >
                    Plan your leave <Icon name="arrow" />
                  </button>
                </div>
                <div className="landscape" aria-hidden="true">
                  <div className="sun" />
                  <div className="hill hill-back" />
                  <div className="hill hill-front" />
                  <div className="plant">✳</div>
                </div>
              </div>
              <div className="quick-card">
                <div className="section-kicker">QUICK ACTIONS</div>
                <button onClick={() => openForm("Claim")}>
                  <span className="quick-icon">
                    <Icon name="claim" />
                  </span>
                  <span>
                    <strong>Submit a claim</strong>
                    <small>Work expenses, taken care of</small>
                  </span>
                  <span>↗</span>
                </button>
                <button onClick={() => navigate("My leave")}>
                  <span className="quick-icon">
                    <Icon name="leave" />
                  </span>
                  <span>
                    <strong>Check my leave</strong>
                    <small>Your requests and time off</small>
                  </span>
                  <span>↗</span>
                </button>
              </div>
            </section>
          )}
          {view === "My timesheet" && (
            <section className="requests-card timesheet-card">
              <div className="requests-heading">
                <div>
                  <h2>Monthly timesheet</h2>
                  <p>Monday–Friday · 8 hours per day, excluding lunch</p>
                </div>
                <label className="month-picker">
                  Month
                  <input
                    aria-label="Timesheet month"
                    type="month"
                    value={month}
                    onChange={(e) => {
                      if (dirtyTimesheet) {
                        setMessage(
                          "Save the current month before changing months.",
                        );
                        return;
                      }
                      setMonth(e.target.value);
                    }}
                  />
                </label>
              </div>
              <p className="timesheet-help">
                Enter actual worked hours. Approved leave is added
                automatically; pending leave stays separate. Public holidays are
                not configured yet.
              </p>
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Day</th>
                      <th>Worked hours</th>
                      <th>Approved leave</th>
                      <th>Pending leave</th>
                      <th>Total / status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {monthDates.map((date) => {
                      const weekday = isWeekday(date);
                      const hours = (status: string) =>
                        mine
                          .filter((r) => r.status === status)
                          .flatMap((r) => r.entries ?? [])
                          .filter((e) => e.date === date)
                          .reduce((sum, e) => sum + e.hours, 0);
                      const approved = hours("Approved"),
                        awaiting = hours("Pending");
                      const raw = worked[date] ?? (weekday ? "" : "0");
                      const amount = Number(raw || 0);
                      const total = amount + approved;
                      const invalid =
                        !Number.isFinite(amount) || amount < 0 || amount > 24;
                      const warning = invalid
                        ? "Enter 0–24 hours"
                        : total > (weekday || approved ? 8 : 0)
                          ? "Review extra hours"
                          : weekday && total < 8
                            ? "Incomplete"
                            : weekday
                              ? "Complete"
                              : approved
                                ? "Certified leave"
                                : "Weekend";
                      return (
                        <tr
                          key={date}
                          className={!weekday ? "weekend-row" : ""}
                        >
                          <td>{dateLabel(date)}</td>
                          <td>
                            {new Date(date + "T00:00:00Z").toLocaleDateString(
                              "en-GB",
                              { weekday: "short", timeZone: "UTC" },
                            )}
                          </td>
                          <td>
                            <input
                              disabled={!loaded || busy}
                              className="hours-input"
                              aria-label={`Worked hours ${date}`}
                              type="number"
                              min="0"
                              max="24"
                              step="any"
                              placeholder="0"
                              value={raw}
                              onChange={(e) => {
                                const next = e.target.value;
                                const amount = Number(next);
                                if (
                                  next !== "" &&
                                  (!Number.isFinite(amount) ||
                                    amount < 0 ||
                                    amount > 24)
                                ) {
                                  setMessage(
                                    "Worked hours must be between 0 and 24.",
                                  );
                                  return;
                                }
                                if (amount + approved > 8 && approved > 0) {
                                  setMessage(
                                    "Worked hours plus approved leave cannot exceed 8 hours on this date.",
                                  );
                                  return;
                                }
                                setWorked((previous) => ({
                                  ...previous,
                                  [date]: next,
                                }));
                                setDirtyTimesheet(true);
                              }}
                            />
                          </td>
                          <td>{approved} h</td>
                          <td>{awaiting ? `${awaiting} h pending` : "—"}</td>
                          <td>
                            <strong>
                              {invalid
                                ? "—"
                                : `${total} / ${weekday || approved ? 8 : 0} h`}
                            </strong>
                            <small
                              className={`request-note ${warning === "Complete" ? "complete-hours" : ""}`}
                            >
                              {warning}
                            </small>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
              <div className="table-footer">
                <span>
                  Worked:{" "}
                  {monthDates.reduce(
                    (sum, date) => sum + (Number(worked[date]) || 0),
                    0,
                  )}{" "}
                  h
                </span>
                <button
                  className="button primary"
                  disabled={!loaded || busy || !dirtyTimesheet}
                  onClick={saveTimesheet}
                >
                  {busy
                    ? "Saving…"
                    : dirtyTimesheet
                      ? "Save timesheet"
                      : "Timesheet saved"}
                </button>
              </div>
            </section>
          )}
          {view !== "My timesheet" && view !== "Employees" && (
            <section className="requests-card">
              <div className="requests-heading">
                <div>
                  <h2>
                    {view === "HR approvals"
                      ? "Employee requests"
                      : view === "Dashboard"
                        ? "Recent activity"
                        : view === "My leave"
                          ? "Leave requests"
                          : "Expense claims"}
                  </h2>
                  <p>
                    {view === "HR approvals"
                      ? `${pending.length} requests awaiting your decision`
                      : "Every request, and where it stands."}
                  </p>
                </div>
                <div className="filters" aria-label="Filter by status">
                  {["All", "Pending", "Approved", "Rejected"].map((status) => (
                    <button
                      key={status}
                      className={filter === status ? "selected" : ""}
                      onClick={() => setFilter(status)}
                    >
                      {status}
                    </button>
                  ))}
                </div>
              </div>
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>
                        {view === "HR approvals"
                          ? "Employee / request"
                          : "Request"}
                      </th>
                      <th>Details</th>
                      <th>Hours / days / amount</th>
                      <th>Status</th>
                      {view === "HR approvals" && <th>Decision</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {visible.map((request) => (
                      <tr key={request.id}>
                        <td>
                          <div className="request-name">
                            <span
                              className={`request-icon ${request.kind.toLowerCase()}`}
                            >
                              <Icon
                                name={
                                  request.kind === "Leave" ? "leave" : "claim"
                                }
                              />
                            </span>
                            <div>
                              <strong>
                                {view === "HR approvals"
                                  ? request.person
                                  : request.category}
                              </strong>
                              <small>
                                {view === "HR approvals"
                                  ? request.category
                                  : request.kind === "Leave"
                                    ? "Time off request"
                                    : "Expense reimbursement"}
                              </small>
                            </div>
                          </div>
                        </td>
                        <td>
                          <span>{request.detail}</span>
                          <small className="request-note">{request.note}</small>
                        </td>
                        <td className="amount">
                          {request.kind === "Leave"
                            ? `${request.value * 8} h (${request.value} ${request.value === 1 ? "day" : "days"})`
                            : money(request.value)}
                        </td>
                        <td>
                          <span
                            className={`status ${request.status.toLowerCase()}`}
                          >
                            <span />
                            {request.status}
                          </span>
                        </td>
                        {view === "HR approvals" && (
                          <td>
                            {request.status === "Pending" ? (
                              <div className="decision-buttons">
                                <button
                                  disabled={busy}
                                  onClick={() => decide(request.id, "Approved")}
                                >
                                  Approve
                                </button>
                                <button
                                  disabled={busy}
                                  onClick={() => decide(request.id, "Rejected")}
                                >
                                  Reject
                                </button>
                              </div>
                            ) : (
                              <span className="reviewed">Reviewed</span>
                            )}
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
                {visible.length === 0 && (
                  <div className="empty-state">
                    No requests here yet. Your next request will appear here.
                  </div>
                )}
              </div>
              <div className="table-footer">
                <span>{visible.length} request(s)</span>
                <span>Reviewed by HR or Admin</span>
              </div>
            </section>
          )}
          {view === "Employees" && canManageEmployees && (
            <section className="requests-card">
              <div className="requests-heading">
                <div>
                  <h2>Employees</h2>
                  <p>
                    HR can add Employee and HR profiles and edit Employee
                    profiles. Admin manages all roles.
                  </p>
                </div>
              </div>
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>Name</th>
                      <th>Email</th>
                      <th>Join date</th>
                      <th>Role</th>
                      <th>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {employees.map((person) => (
                      <tr key={person.id}>
                        <td>{person.name}</td>
                        <td>{person.email}</td>
                        <td>{dateLabel(person.join_date)}</td>
                        <td>{person.role}</td>
                        <td>
                          <button
                            className="button secondary"
                            disabled={
                              busy ||
                              !loaded ||
                              (employee.role !== "Admin" &&
                                person.role !== "Employee")
                            }
                            onClick={() => {
                              setError("");
                              setEmployeeEditor(person);
                            }}
                          >
                            Edit
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
          <footer className="page-footer">
            <span>
              People & co.{" "}
              <span> · A little less admin, a little more life.</span>
            </span>
            <span>Local demo · Database storage · Login not yet enabled</span>
          </footer>
        </main>
      </div>
      {employeeEditor && canManageEmployees && (
        <div className="modal-backdrop">
          <section
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="employee-title"
            onKeyDown={(event) => {
              if (event.key === "Escape" && !busy) setEmployeeEditor(null);
            }}
          >
            <div className="modal-header">
              <h2 id="employee-title">
                {employeeEditor.id ? "Edit employee" : "Add employee"}
              </h2>
              <button
                disabled={busy}
                aria-label="Close employee form"
                onClick={() => setEmployeeEditor(null)}
              >
                ×
              </button>
            </div>
            <p className="modal-intro">
              Create a profile now. Login credentials will be added with
              authentication.
            </p>
            <form onSubmit={saveEmployee}>
              <label>
                Name
                <input
                  autoFocus
                  name="name"
                  defaultValue={employeeEditor.name ?? ""}
                  maxLength={100}
                  required
                />
              </label>
              <label>
                Email
                <input
                  name="email"
                  type="email"
                  defaultValue={employeeEditor.email ?? ""}
                  maxLength={254}
                  disabled={Boolean(employeeEditor.id)}
                  required
                />
              </label>
              <div className="form-row">
                <label>
                  Join date
                  <input
                    name="join_date"
                    type="date"
                    defaultValue={employeeEditor.join_date ?? ""}
                    required
                  />
                </label>
                <label>
                  Role
                  <select
                    name="role"
                    defaultValue={employeeEditor.role ?? "Employee"}
                  >
                    <option>Employee</option>
                    {(employee.role === "Admin" || !employeeEditor.id) && (
                      <option>HR</option>
                    )}
                    {employee.role === "Admin" && <option>Admin</option>}
                  </select>
                </label>
              </div>
              {error && (
                <p className="form-error" role="alert">
                  {error}
                </p>
              )}
              <div className="form-actions">
                <button
                  type="button"
                  className="button secondary"
                  disabled={busy}
                  onClick={() => setEmployeeEditor(null)}
                >
                  Cancel
                </button>
                <button className="button primary" disabled={busy || !loaded}>
                  {busy ? "Saving…" : "Save employee"}
                </button>
              </div>
            </form>
          </section>
        </div>
      )}
      {modal && (
        <div
          className="modal-backdrop"
          onClick={(event) => {
            if (event.target === event.currentTarget) setModal(null);
          }}
        >
          <section
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="form-title"
            onKeyDown={(event) => {
              if (event.key === "Escape") setModal(null);
              if (event.key === "Tab") {
                const elements = Array.from(
                  event.currentTarget.querySelectorAll<HTMLElement>(
                    "button, input, select, textarea",
                  ),
                );
                const first = elements[0];
                const last = elements[elements.length - 1];
                if (event.shiftKey && document.activeElement === first) {
                  event.preventDefault();
                  last.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                  event.preventDefault();
                  first.focus();
                }
              }
            }}
          >
            <div className="modal-header">
              <div>
                <span className="eyebrow">YOUR NEXT REQUEST</span>
                <h2 id="form-title">
                  {modal === "Leave"
                    ? "Make time for yourself."
                    : "Submit an expense claim."}
                </h2>
              </div>
              <button aria-label="Close form" onClick={() => setModal(null)}>
                ×
              </button>
            </div>
            <p className="modal-intro">
              Your request will go to HR for approval.
            </p>
            <form onSubmit={submit}>
              <label>
                Category
                <select
                  name="category"
                  value={modal === "Leave" ? leaveCategory : undefined}
                  onChange={(e) => {
                    if (modal === "Leave") {
                      setLeaveCategory(e.target.value);
                      setLeaveHours({});
                    }
                  }}
                >
                  {(modal === "Leave"
                    ? [
                        "Annual leave",
                        "Medical leave",
                        "Hospitalisation leave",
                        "Unpaid leave",
                      ]
                    : ["Travel", "Meals", "Office supplies", "Other"]
                  ).map((category) => (
                    <option key={category}>{category}</option>
                  ))}
                </select>
              </label>
              {modal === "Leave" ? (
                <>
                  <div className="form-row">
                    <label>
                      Start date
                      <input
                        autoFocus
                        name="start"
                        type="date"
                        value={startDate}
                        onInput={(e) => {
                          setStartDate(e.currentTarget.value);
                          setLeaveHours({});
                        }}
                        required
                      />
                    </label>
                    <label>
                      End date
                      <input
                        name="end"
                        type="date"
                        min={startDate}
                        value={endDate}
                        onInput={(e) => {
                          setEndDate(e.currentTarget.value);
                          setLeaveHours({});
                        }}
                        required
                      />
                    </label>
                  </div>
                  <p className="field-help">
                    {isMedical
                      ? "Medical leave follows certificate dates in full days. HR must verify certification and eligibility."
                      : "Weekends excluded · Choose any positive number of hours, up to 8 per working day. 8 hours = 1 day. Company public holidays are not configured yet."}
                  </p>
                  {leaveDates.length > 0 && (
                    <div className="leave-hours-list">
                      {leaveDates.map((date) => (
                        <label key={date}>
                          {dateLabel(date)}
                          <input
                            aria-label={`Leave hours ${date}`}
                            type="number"
                            min="0"
                            max="8"
                            step="any"
                            required
                            readOnly={isMedical}
                            value={isMedical ? "8" : (leaveHours[date] ?? "8")}
                            onChange={(e) =>
                              setLeaveHours((previous) => ({
                                ...previous,
                                [date]: e.target.value,
                              }))
                            }
                          />
                        </label>
                      ))}
                      <p>
                        Total:{" "}
                        {leaveDates.reduce(
                          (sum, date) =>
                            sum +
                            (isMedical ? 8 : Number(leaveHours[date] ?? 8)),
                          0,
                        )}{" "}
                        hours (
                        {leaveDates.reduce(
                          (sum, date) =>
                            sum +
                            (isMedical ? 8 : Number(leaveHours[date] ?? 8)),
                          0,
                        ) / 8}{" "}
                        days)
                      </p>
                    </div>
                  )}
                </>
              ) : (
                <>
                  <label>
                    Description
                    <input
                      autoFocus
                      name="description"
                      placeholder="e.g. Transport to client meeting"
                      required
                      maxLength={120}
                    />
                  </label>
                  <label>
                    Amount (RM)
                    <input
                      name="amount"
                      type="number"
                      min="0.01"
                      max="100000"
                      step="0.01"
                      placeholder="0.00"
                      required
                    />
                  </label>
                  <p className="field-help">
                    Receipt uploads will be added when the database is
                    connected.
                  </p>
                </>
              )}
              <label>
                {modal === "Leave" ? "Reason" : "Additional details"}
                <textarea
                  name="note"
                  placeholder="Add a little context for HR…"
                  required
                  maxLength={500}
                  rows={3}
                />
              </label>
              {error && (
                <p className="form-error" role="alert">
                  {error}
                </p>
              )}
              <div className="form-actions">
                <button
                  type="button"
                  className="button secondary"
                  onClick={() => setModal(null)}
                >
                  Cancel
                </button>
                <button
                  className="button primary"
                  type="submit"
                  disabled={busy || !loaded}
                >
                  {busy ? "Saving…" : "Submit for approval"}{" "}
                  <Icon name="arrow" />
                </button>
              </div>
            </form>
          </section>
        </div>
      )}
    </div>
  );
}
