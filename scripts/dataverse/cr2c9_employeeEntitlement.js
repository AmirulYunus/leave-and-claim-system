/* Read-only rate preview. The server reads saved HR inputs as the caller. */
var EmployeeEntitlement = (function () {
  "use strict";
  var fields = ["cr2c9_datejoined", "cr2c9_employeelevel", "cr2c9_employmenttype"];
  var states = new WeakMap();
  var notification = "leave-entitlement-preview";
  var sickType = "261e2bd3-770e-4880-bcef-e4f5be6571e6";
  var hospitalType = "91f9367f-5bd7-4a06-a750-124c36d5729f";
  function PreviewRequest(id) { this.entity = { entityType: "cr2c9_employee", id: id }; }
  PreviewRequest.prototype.getMetadata = function () {
    return { boundParameter: "entity", operationName: "cr2c9_GetEntitlementPreview", operationType: 1,
      parameterTypes: { entity: { typeName: "mscrm.cr2c9_employee", structuralProperty: 5 } } };
  };
  function rate(label, value) {
    if (!value || typeof value.Status !== "string") throw new Error("Invalid preview response");
    if (value.Status === "Calculated") {
      if (!Number.isInteger(value.Days) || value.Days < 0) throw new Error("Invalid rate");
      return label + ": " + value.Days + " days";
    }
    return label + ": " + value.Reason;
  }
  async function allRows(table, options) {
    var rows = [], pages = 0;
    do {
      if (++pages > 5) throw new Error("Medical balance history needs review");
      var page = await Xrm.WebApi.online.retrieveMultipleRecords(table, options, 5000);
      rows.push.apply(rows, page.entities);
      options = page.nextLink ? page.nextLink.substring(page.nextLink.indexOf("?")) : null;
    } while (options);
    return rows;
  }
  async function usedHours(id, start, end, leaveType, label) {
    var periods = await allRows("cr2c9_leavebalanceperiod",
      "?$select=cr2c9_leavebalanceperiodid,cr2c9_periodstart,cr2c9_periodend" +
      "&$filter=_cr2c9_employee_value eq " + id + " and _cr2c9_leavetype_value eq " + leaveType);
    var matches = periods.filter(function (p) { return p.cr2c9_periodstart <= end && p.cr2c9_periodend >= start; });
    if (!matches.length) return null;
    if (matches.length !== 1 || matches[0].cr2c9_periodstart !== start || matches[0].cr2c9_periodend !== end)
      throw new Error(label + " balance period needs review");
    var entries = await allRows("cr2c9_leavebalanceentry",
      "?$select=cr2c9_hours,cr2c9_entrykind,cr2c9_operationkey" +
      "&$filter=_cr2c9_balanceperiod_value eq " + matches[0].cr2c9_leavebalanceperiodid);
    var keys = new Set(), granted = 0, used = 0;
    entries.forEach(function (entry) {
      var hours = entry.cr2c9_hours, key = entry.cr2c9_operationkey;
      if (!Number.isSafeInteger(hours) || !key || keys.has(key)) throw new Error(label + " ledger needs review");
      keys.add(key);
      if (entry.cr2c9_entrykind === 290040000 && hours > 0) granted += hours;
      else if (entry.cr2c9_entrykind === 290040001 && hours < 0) used -= hours;
      else if (entry.cr2c9_entrykind === 290040002 && hours > 0) used -= hours;
      else throw new Error(label + " ledger needs review");
    });
    if (!Number.isSafeInteger(used) || !Number.isSafeInteger(granted) || used < 0 || used > granted || used % 4)
      throw new Error(label + " ledger needs review");
    return used;
  }
  async function companyMedicalFigure(id, assessmentDate) {
    var year = assessmentDate.slice(0, 4), start = year + "-01-01", end = year + "-12-31";
    var medical = await usedHours(id, start, end, sickType, "Medical");
    var hospital = await usedHours(id, start, end, hospitalType, "Hospitalisation");
    if (medical === null && hospital === null)
      return "Company combined figure awaits recorded Medical and Hospitalisation balance periods.";
    if (medical === null)
      return "Company combined figure awaits a recorded Medical Leave balance period.";
    if (hospital === null)
      return "Company 60-day combined figure, less " + (medical / 8) +
        " Medical days used: " + (Math.max(0, 480 - medical) / 8) +
        " days before Hospitalisation is recorded. This is not an approval limit.";
    return "Company 60-day combined figure: " +
      (Math.max(0, 480 - medical - hospital) / 8) + " days after " +
      (medical / 8) + " Medical and " + (hospital / 8) +
      " Hospitalisation days used. This is not an approval limit.";
  }
  function invalidate(form, state) {
    state.sequence++;
    form.ui.setFormNotification("Save HR employment details to refresh the entitlement preview.", "INFO", notification);
  }
  async function refresh(form, state) {
    var sequence = ++state.sequence;
    var id = (form.data.entity.getId() || "").replace(/[{}]/g, "");
    if (!id) { form.ui.setFormNotification("Save this employee before calculating entitlement rates.", "INFO", notification); return; }
    if (form.data.entity.getIsDirty()) { invalidate(form, state); return; }
    form.ui.setFormNotification("Checking entitlement inputs…", "INFO", notification);
    var timer;
    try {
      var timeout = new Promise(function (_, reject) { timer = setTimeout(function () { reject(new Error("Preview timeout")); }, 10000); });
      var pending = (async function () {
        var response = await Xrm.WebApi.online.execute(new PreviewRequest(id));
        if (!response.ok) throw new Error("Preview request failed");
        return JSON.parse((await response.json()).PreviewJson);
      }());
      var preview = await Promise.race([pending, timeout]);
      if (sequence !== state.sequence || form.data.entity.getIsDirty()) return;
      if (!/^\d{4}-\d{2}-\d{2}$/.test(preview.AssessmentDate)) throw new Error("Assessment date missing");
      var company = "";
      if (preview.Sick.Status === "Calculated") {
        try { company = " " + await companyMedicalFigure(id, preview.AssessmentDate); }
        catch (_) { company = " Company combined figure unavailable; review the Medical and Hospitalisation ledgers."; }
      }
      if (sequence !== state.sequence || form.data.entity.getIsDirty()) return;
      var message = "Full-year rates as of " + preview.AssessmentDate + " (Malaysia): " +
        rate("Annual", preview.Annual) + "; " + rate("Medical", preview.Sick) + "; " +
        rate("Hospitalisation", preview.Hospitalisation) + ". These full-year rates are not an available balance; current-year annual leave is earned monthly and shown on the Annual balance tab." + company;
      var complete = [preview.Annual, preview.Sick, preview.Hospitalisation].every(function (r) { return r.Status === "Calculated"; });
      form.ui.setFormNotification(message, complete ? "INFO" : "WARNING", notification);
    } catch (_) {
      if (sequence === state.sequence) form.ui.setFormNotification("Entitlement preview unavailable. Refresh to retry; no entitlement or balance was changed.", "WARNING", notification);
    } finally { clearTimeout(timer); }
  }
  function onLoad(context) {
    var form = context.getFormContext();
    var state = states.get(form);
    if (!state) {
      state = { sequence: 0 }; states.set(form, state);
      fields.forEach(function (name) {
        var attribute = form.getAttribute(name);
        if (attribute) attribute.addOnChange(function () { invalidate(form, state); });
      });
      form.data.addOnLoad(function () { refresh(form, state); });
    }
    // UI protection only; separate Dataverse field/role authorization is pending.
    ["cr2c9_entrauserid", "cr2c9_superior", "cr2c9_annualleaveentitlementdays"].forEach(function (name) {
      var control = form.getControl(name); if (control) control.setDisabled(true);
    });
    return refresh(form, state);
  }
  return { onLoad: onLoad };
}());
if (typeof module !== "undefined") module.exports = EmployeeEntitlement;
