/* Approved leave cancellation and HR Emergency correction. Register onLoad
 * and onSave with execution context and keep Async OnSave enabled.
 * The server guard is authoritative; this form keeps its review UX in sync. */
var LeaveCancellationForm = (function () {
  "use strict";
  var states = new WeakMap();
  var notice = "cr2c9_cancellation_review";
  function guid(v) { return String(v || "").replace(/[{}]/g, "").toLowerCase(); }
  function hasHrRole(ctx) {
    var roles = ctx.userSettings.roles;
    return Boolean(roles && typeof roles.getAll === "function" && roles.getAll().some(function (role) {
      return role.name === "Leave HR";
    }));
  }
  function bounded(p) {
    return new Promise(function (resolve, reject) {
      var timer = setTimeout(function () { reject(new Error("Verification timed out. Refresh and try again.")); }, 8000);
      p.then(function (value) { clearTimeout(timer); resolve(value); }, function (error) { clearTimeout(timer); reject(error); });
    });
  }
  function message(form, text, level) { form.ui.setFormNotification(text, level || "ERROR", notice); }
  function required(form) {
    return ["cr2c9_status", "cr2c9_leaverequest", "cr2c9_reason", "cr2c9_decisioncomments", "cr2c9_expectedversion"]
      .every(function (name) { return Boolean(form.getAttribute(name)); });
  }
  function lock(form) {
    form.ui.controls.forEach(function (control) {
      if (typeof control.setDisabled === "function") control.setDisabled(true);
    });
  }
  function render(form, state) {
    lock(form);
    if (form.ui.getFormType() === 1 && hasHrRole(Xrm.Utility.getGlobalContext()) && !state.saving) {
      ["cr2c9_leaverequest", "cr2c9_reason"].forEach(function (name) {
        form.getAttribute(name).controls.forEach(function (control) { control.setDisabled(false); });
      });
      return;
    }
    var editable = state.authorized && !state.saving && state.baseline === 0;
    ["cr2c9_status", "cr2c9_decisioncomments"].forEach(function (name) {
      form.getAttribute(name).controls.forEach(function (control) { control.setDisabled(!editable); });
    });
  }
  function verify(form) {
    var id = guid(form.data.entity.getId());
    if (!id || form.ui.getFormType() !== 2) return Promise.reject(new Error("Only saved pending cancellations can be reviewed here."));
    var userId = guid(Xrm.Utility.getGlobalContext().userSettings.userId);
    return Promise.all([
      Xrm.WebApi.retrieveRecord("cr2c9_leavecancellation", id, "?$select=cr2c9_status,_cr2c9_leaverequest_value"),
      Xrm.WebApi.retrieveRecord("systemuser", userId, "?$select=azureactivedirectoryobjectid,fullname")
    ]).then(function (rows) {
      var saved = rows[0], actor = rows[1];
      var version = /^W\/"(\d+)"$/.exec(String(saved["@odata.etag"] || ""));
      if (!version || saved.cr2c9_status !== 0) throw new Error("This cancellation has changed. Refresh the form.");
      var requestId = guid(saved._cr2c9_leaverequest_value), identity = guid(actor.azureactivedirectoryobjectid);
      var shown = form.getAttribute("cr2c9_leaverequest").getValue();
      if (!requestId || !shown || shown.length !== 1 || guid(shown[0].id) !== requestId)
        throw new Error("The cancellation's leave request changed. Refresh the form.");
      if (!identity || !actor.fullname) throw new Error("Your Entra identity could not be verified.");
      return Xrm.WebApi.retrieveRecord("cr2c9_leaverequest", requestId,
        "?$select=cr2c9_status,_cr2c9_employeename_value").then(function (request) {
        if (request.cr2c9_status !== 1) throw new Error("The original leave is no longer Approved.");
        var employeeId = guid(request._cr2c9_employeename_value);
        if (!employeeId) throw new Error("The original employee is missing.");
        return Xrm.WebApi.retrieveRecord("cr2c9_employee", employeeId,
          "?$select=cr2c9_entrauserid,_cr2c9_superior_value").then(function (employee) {
          var managerId = guid(employee._cr2c9_superior_value);
          if (guid(employee.cr2c9_entrauserid) === identity)
            throw new Error("You cannot decide this cancellation.");
          if (hasHrRole(Xrm.Utility.getGlobalContext())) return { version: version[1] };
          if (managerId === employeeId)
            throw new Error("An employee cannot decide their own cancellation. Ask HR to review it.");
          if (!managerId) throw new Error("No synced Superior is assigned. Ask HR to review this cancellation.");
          return Xrm.WebApi.retrieveRecord("cr2c9_employee", managerId, "?$select=cr2c9_entrauserid")
            .then(function (manager) {
              if (guid(manager.cr2c9_entrauserid) !== identity)
                throw new Error("Only the employee's latest synced Superior or HR can decide this cancellation.");
              return { version: version[1] };
            });
        });
      });
    });
  }
  function refresh(form, state) {
    var generation = ++state.generation;
    state.authorized = false;
    render(form, state);
    if (form.ui.getFormType() === 1) {
      message(form, hasHrRole(Xrm.Utility.getGlobalContext())
        ? "HR correction: select the approved Emergency Leave request and record why it must be reversed. Save this pending record, then review and approve it to restore the original balance."
        : "Only HR may create a correction here. Employees request future cancellations in Employee self service.", "INFO");
      return;
    }
    if (state.baseline !== 0 || form.ui.getFormType() !== 2) {
      message(form, "This review form is read-only outside a pending cancellation.", "INFO");
      return;
    }
    message(form, "Checking Superior or HR review access…", "INFO");
    bounded(verify(form)).then(function (scope) {
      if (generation !== state.generation) return;
      state.authorized = true;
      state.version = scope.version;
      render(form, state);
      form.ui.clearFormNotification(notice);
    }).catch(function (error) {
      if (generation !== state.generation) return;
      message(form, error.message || "Reviewer verification failed. Decision blocked.");
    });
  }
  function onLoad(ctx) {
    var form = ctx.getFormContext();
    if (!required(form)) { lock(form); message(form, "Review form setup is incomplete."); return; }
    var state = states.get(form), status = form.getAttribute("cr2c9_status");
    if (state) { refresh(form, state); return; }
    state = { baseline: status.getValue(), authorized: false, saving: false, version: null, generation: 0 };
    states.set(form, state);
    if (form.ui.getFormType() === 1) {
      status.setValue(0);
      status.setSubmitMode("always");
    }
    form.data.addOnLoad(function () { state.baseline = status.getValue(); refresh(form, state); });
    form.data.entity.addOnPostSave(function (event) {
      if (event.getEventArgs().getIsSaveSuccess()) {
        state.baseline = status.getValue(); state.authorized = false; render(form, state);
      }
    });
    refresh(form, state);
  }
  function onSave(ctx) {
    var form = ctx.getFormContext(), args = ctx.getEventArgs(), state = states.get(form);
    function block(text) { args.preventDefault(); message(form, text); }
    if (!state || !required(form)) { block("Review form setup is incomplete."); return; }
    var status = form.getAttribute("cr2c9_status"), target = status.getValue();
    if (form.ui.getFormType() === 1) {
      var request = form.getAttribute("cr2c9_leaverequest").getValue();
      var reason = form.getAttribute("cr2c9_reason").getValue();
      if (!hasHrRole(Xrm.Utility.getGlobalContext()) || target !== 0 || !request || request.length !== 1 ||
          !reason || !String(reason).trim())
        block("HR must select one approved Emergency Leave request and enter a correction reason.");
      return;
    }
    if (state.saving) { block("A decision is already being verified."); return; }
    if (state.baseline !== 0) { if (form.data.entity.getIsDirty()) block("This cancellation is read-only."); return; }
    if (!state.authorized || [1, 3].indexOf(target) === -1) {
      block("Select Approved or Rejected after reviewer verification."); return;
    }
    if (typeof args.preventDefaultOnError !== "function") {
      block("Enable Async OnSave in the Superior app."); return;
    }
    args.preventDefaultOnError();
    state.saving = true;
    ++state.generation;
    render(form, state);
    return bounded(verify(form)).then(function (scope) {
      if (!state.version || scope.version !== state.version || status.getValue() !== target)
        throw new Error("This cancellation changed after review. Refresh it before deciding.");
      var version = form.getAttribute("cr2c9_expectedversion");
      version.setValue(scope.version);
      version.setSubmitMode("always");
      form.ui.clearFormNotification(notice);
    }).catch(function (error) {
      state.authorized = false;
      message(form, error.message || "Decision was not saved.");
      throw error;
    }).finally(function () { state.saving = false; render(form, state); });
  }
  return { onLoad: onLoad, onSave: onSave };
})();
