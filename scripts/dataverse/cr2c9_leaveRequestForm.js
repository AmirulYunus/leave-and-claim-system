/* Latest successfully synced Superior or directly assigned HR reviewer; UI
 * validation is advisory. The server rechecks authority and row version.
 * Register onLoad and onSave with execution context; enable Async OnSave. */
var LeaveRequestForm = (function () {
  "use strict";
  var states = new WeakMap();
  var stamps = ["cr2c9_decisionbyentraid", "cr2c9_decisionbyname", "cr2c9_decisionon"];
  var expectedVersion = "cr2c9_decisionexpectedversion";
  var notice = "cr2c9_superior_check";
  var policyNotice = "cr2c9_review_policy";
  function guid(v) { return String(v || "").replace(/[{}]/g, "").toLowerCase(); }
  function hasHrRole(ctx) {
    var roles = ctx.userSettings.roles;
    return Boolean(roles && typeof roles.getAll === "function" && roles.getAll().some(function (role) {
      return role.name === "Leave HR";
    }));
  }
  function employeeId(form) {
    var v = form.getAttribute("cr2c9_employeename").getValue();
    return v && v.length === 1 && v[0].entityType === "cr2c9_employee" ? guid(v[0].id) : "";
  }
  function message(form, text, level) { form.ui.setFormNotification(text, level || "ERROR", notice); }
  function reviewGuide(form) {
    var type = form.getAttribute("cr2c9_leavetypename");
    var selected = type && type.getValue(), name = selected && selected.length === 1 ? selected[0].name : "";
    var guidance = {
      "Sick Leave": "Before approval, inspect the uploaded medical certificate. The app also checks the 48-hour submission rule.",
      "Medical Leave": "Before approval, inspect the uploaded medical certificate. The app also checks the 48-hour submission rule.",
      "Hospitalisation Leave": "Before approval, inspect the uploaded medical evidence. The separate Hospitalisation balance is checked when you approve.",
      "Marriage Leave": "Before approval, verify the marriage certificate, first-legal-marriage declaration and completed probation.",
      "Compassionate Leave": "Before approval, verify the family relationship and uploaded supporting document. The request must cover three consecutive full calendar days.",
      "Training Leave": "Check the training category. Company-required training does not deduct Annual Leave; employee-requested external training does. Verify that the category matches the request before approving.",
      "Unpaid Leave": "The employee chose leave without pay. Confirm the written reason and dates. Approval does not deduct Annual or any other leave balance.",
      "Maternity Leave": "Before approval, review the expected confinement and formal-notice dates, eligibility and any notice exception. A separate HR verification stamp is not required.",
      "Paternity Leave": "Before approval, verify the birth evidence, marriage, notice circumstances and prior confinement history. A separate HR verification stamp is not required."
    }[name];
    form.ui.clearFormNotification(policyNotice);
    if (guidance) form.ui.setFormNotification(guidance, "INFO", policyNotice);
  }
  function showCategoryFields(form) {
    var type = form.getAttribute("cr2c9_leavetypename");
    var selected = type && type.getValue();
    var name = selected && selected.length === 1 ? selected[0].name : "";
    var fieldsByType = {
      "Compassionate Leave": ["cr2c9_compassionaterelationship"],
      "Marriage Leave": ["cr2c9_marriageregisteredon", "cr2c9_firstlegalmarriage"],
      "Maternity Leave": ["cr2c9_expectedconfinementdate", "cr2c9_maternityformalnoticedate"],
      "Paternity Leave": ["cr2c9_paternityexpecteddate", "cr2c9_paternitybirthdate", "cr2c9_paternitynoticedate"],
      "Training Leave": ["cr2c9_trainingcategory"]
    };
    Object.keys(fieldsByType).forEach(function (leaveType) {
      fieldsByType[leaveType].forEach(function (field) {
        var attribute = form.getAttribute(field);
        if (attribute) attribute.controls.forEach(function (control) {
          if (typeof control.setVisible === "function") control.setVisible(name === leaveType);
        });
      });
    });
  }
  function hideObsoleteVerification(form) {
    ["cr2c9_maternityhrverifiedon", "cr2c9_paternityhrverifiedon"].forEach(function (name) {
      var attribute = form.getAttribute(name);
      if (attribute) attribute.controls.forEach(function (control) {
        if (typeof control.setVisible === "function") control.setVisible(false);
        if (typeof control.setDisabled === "function") control.setDisabled(true);
      });
    });
  }
  function required(form) {
    return ["cr2c9_status", "cr2c9_employeename", "cr2c9_decisioncomments", expectedVersion].concat(stamps).every(function (n) { return Boolean(form.getAttribute(n)); });
  }
  function bounded(promise) {
    return new Promise(function (resolve, reject) {
      var timer = setTimeout(function () { reject(new Error("Verification timed out. Try again.")); }, 8000);
      promise.then(function (v) { clearTimeout(timer); resolve(v); }, function (e) { clearTimeout(timer); reject(e); });
    });
  }
  function verify(form, baseline) {
    var ctx = Xrm.Utility.getGlobalContext(), id = guid(form.data.entity.getId());
    if (!id || form.ui.getFormType() !== 2) return Promise.reject(new Error("Only existing pending requests can be decided."));
    return Promise.all([
      Xrm.WebApi.retrieveRecord("cr2c9_leaverequest", id, "?$select=cr2c9_status,_cr2c9_employeename_value"),
      Xrm.WebApi.retrieveRecord("systemuser", guid(ctx.userSettings.userId), "?$select=azureactivedirectoryobjectid,fullname")
    ]).then(function (rows) {
      var request = rows[0], user = rows[1], employee = guid(request._cr2c9_employeename_value), userId = guid(user.azureactivedirectoryobjectid);
      var version = /^W\/"(\d+)"$/.exec(String(request["@odata.etag"] || ""));
      if (!version) throw new Error("The saved request version could not be verified. Refresh the form.");
      if (request.cr2c9_status !== 0 || baseline !== 0) throw new Error("This request is no longer Pending Superior. Refresh the form.");
      if (!employee || employee !== employeeId(form)) throw new Error("Employee must match the saved request. Refresh the form.");
      if (!userId || !user.fullname) throw new Error("Your Entra identity could not be verified.");
      return Xrm.WebApi.retrieveRecord("cr2c9_employee", employee, "?$select=_cr2c9_superior_value,cr2c9_entrauserid").then(function (row) {
        var superior = guid(row._cr2c9_superior_value);
        if (guid(row.cr2c9_entrauserid) === userId || superior === employee) throw new Error("You cannot decide your own leave request.");
        if (hasHrRole(ctx)) return { id: userId, name: user.fullname, version: version[1] };
        if (!superior) throw new Error("No synced Superior is assigned. Ask for the employee sync to be checked.");
        return Xrm.WebApi.retrieveRecord("cr2c9_employee", superior, "?$select=cr2c9_entrauserid").then(function (manager) {
          if (!guid(manager.cr2c9_entrauserid) || guid(manager.cr2c9_entrauserid) !== userId) throw new Error("Only the employee's latest synced Superior or HR can decide this request.");
          return { id: userId, name: user.fullname, version: version[1] };
        });
      });
    });
  }
  function lock(form) {
    form.ui.controls.forEach(function (c) { if (typeof c.setDisabled === "function") c.setDisabled(true); });
  }
  function render(form, state) {
    var status = form.getAttribute("cr2c9_status"), editable = state.authorized && !state.saving && state.baseline === 0;
    lock(form);
    status.controls.forEach(function (c) {
      c.clearOptions();
      state.options.forEach(function (o) { if (editable ? [0, 1, 3].indexOf(o.value) !== -1 : o.value !== 4 || state.baseline === 4) c.addOption(o); });
      c.setDisabled(!editable);
    });
    form.getAttribute("cr2c9_decisioncomments").controls.forEach(function (c) { c.setDisabled(!editable); });
  }
  function refresh(form, state) {
    var generation = ++state.generation;
    state.authorized = false;
    state.expectedVersion = null;
    render(form, state);
    if (state.baseline !== 0 || form.ui.getFormType() !== 2) { message(form, "This review form is read-only for requests outside Pending Superior.", "INFO"); return; }
    message(form, "Checking your Superior or HR review access…", "INFO");
    bounded(verify(form, state.baseline)).then(function (user) {
      if (generation !== state.generation) return;
      state.authorized = true;
      state.expectedVersion = user.version;
      render(form, state);
      form.ui.clearFormNotification(notice);
    }).catch(function (e) {
      if (generation !== state.generation) return;
      state.authorized = false;
      render(form, state);
      message(form, e.message || "Reviewer verification failed. Decisions are blocked.");
    });
  }
  function onLoad(ctx) {
    var form = ctx.getFormContext();
    hideObsoleteVerification(form);
    reviewGuide(form);
    showCategoryFields(form);
    var type = form.getAttribute("cr2c9_leavetypename");
    if (type) type.addOnChange(function () { reviewGuide(form); showCategoryFields(form); });
    if (!required(form)) { lock(form); message(form, "Review setup incomplete: add Employee, Status, Comments, decision stamps and Expected Version to the form."); return; }
    var state = states.get(form), status = form.getAttribute("cr2c9_status");
    if (state) { refresh(form, state); return; }
    state = { baseline: status.getValue(), authorized: false, saving: false, generation: 0, options: status.getOptions().filter(function (o) { return o.value !== null; }) };
    states.set(form, state);
    status.addOnChange(function () { if (!state.authorized || state.baseline !== 0 || [0, 1, 3].indexOf(status.getValue()) === -1) status.setValue(state.baseline); });
    form.getAttribute("cr2c9_employeename").addOnChange(function () { status.setValue(state.baseline); refresh(form, state); });
    form.data.addOnLoad(function () { state.baseline = status.getValue(); showCategoryFields(form); refresh(form, state); });
    form.data.entity.addOnPostSave(function (event) {
      if (event.getEventArgs().getIsSaveSuccess()) { state.baseline = status.getValue(); state.authorized = false; render(form, state); }
    });
    refresh(form, state);
  }
  function onSave(ctx) {
    var form = ctx.getFormContext(), args = ctx.getEventArgs(), state = states.get(form);
    function block(text) { args.preventDefault(); message(form, text); }
    if (!state || !required(form)) { block("Review setup incomplete. Save blocked."); return; }
    var status = form.getAttribute("cr2c9_status"), target = status.getValue();
    if (state.saving) { block("A decision is already being verified. Please wait."); return; }
    if (state.baseline !== 0) { if (form.data.entity.getIsDirty()) block("Decided and other non-pending requests are read-only in this review form."); return; }
    if (target === 0) { if (form.data.entity.getIsDirty()) block("Select Approved or Rejected to save review comments with a decision."); return; }
    if (!state.authorized || [1, 3].indexOf(target) === -1) { block("Only the assigned Superior or HR can save Approved or Rejected."); return; }
    if (typeof args.preventDefaultOnError !== "function") { block("Enable Async OnSave in this app before saving a decision."); return; }
    args.preventDefaultOnError(); // Call synchronously: rejected promise or timeout must cancel save.
    state.saving = true;
    ++state.generation;
    render(form, state);
    var employee = employeeId(form);
    return bounded(verify(form, state.baseline)).then(function (user) {
      if (!state.expectedVersion || user.version !== state.expectedVersion) throw new Error("This request changed after review. Refresh it before deciding.");
      if (status.getValue() !== target || employeeId(form) !== employee) throw new Error("The form changed during verification. Try saving again.");
      form.getAttribute(stamps[0]).setValue(user.id);
      form.getAttribute(stamps[1]).setValue(user.name);
      form.getAttribute(stamps[2]).setValue(new Date());
      stamps.forEach(function (n) { form.getAttribute(n).setSubmitMode("always"); });
      form.getAttribute(expectedVersion).setValue(user.version);
      form.getAttribute(expectedVersion).setSubmitMode("always");
      form.ui.clearFormNotification(notice);
    }).catch(function (e) {
      state.authorized = false;
      message(form, e.message || "Reviewer verification failed. Decision was not saved.");
      throw e;
    }).finally(function () { state.saving = false; render(form, state); });
  }
  return { onLoad: onLoad, onSave: onSave };
})();
