/* Development-only web resource. Register ONLY on Superior Review OnLoad,
 * with 'Pass execution context as first parameter' enabled.
 * No approval endpoint or record update is called by this script.
 */
var LeaveRequestForm = (function () {
  "use strict";

  var TEST = {
    enabled: true,
    environmentUrl: "https://orgf7668341.crm5.dynamics.com",
    managerEntraId: "a8f9e5ea-402e-43a1-812c-dffee674d6f9",
    employeeRowId: "7fa0f1a7-0a38-4bdf-b17b-472f7f66f87c",
  };
  var states = new WeakMap();
  var allowed = [0, 1, 3];
  var bannerId = "cr2c9_development_test";
  var saveId = "cr2c9_test_save_blocked";

  function guid(value) {
    return String(value || "").replace(/[{}]/g, "").toLowerCase();
  }

  function onLoad(executionContext) {
    var form = executionContext.getFormContext();
    var existing = states.get(form);
    if (existing) {
      // Repeated OnLoad must not adopt a simulated value as the saved baseline.
      existing.render();
      return;
    }
    var status = form.getAttribute("cr2c9_status");
    var employee = form.getAttribute("cr2c9_employeename");
    if (!status || !employee) {
      if (status) status.controls.forEach(function (c) { c.setDisabled(true); });
      form.ui.setFormNotification(
        "Development test mode unavailable: add Employee Name and Status to this form.",
        "ERROR", bannerId
      );
      return;
    }

    var context = Xrm.Utility.getGlobalContext();
    var environmentMatches = TEST.enabled &&
      context.getClientUrl().replace(/\/$/, "").toLowerCase() === TEST.environmentUrl;
    var state = {
      initialStatus: status.getValue(),
      verified: false,
      options: status.getOptions().filter(function (o) { return o.value !== null; }),
    };

    function eligible() {
      var lookup = employee.getValue();
      return environmentMatches && state.verified && form.ui.getFormType() === 2 &&
        state.initialStatus === 0 && lookup && lookup.length === 1 &&
        lookup[0].entityType === "cr2c9_employee" &&
        guid(lookup[0].id) === TEST.employeeRowId;
    }

    function render() {
      var active = Boolean(eligible());
      if (!active && status.getValue() !== state.initialStatus) {
        status.setValue(state.initialStatus);
      }
      status.controls.forEach(function (control) {
        control.setDisabled(true);
        control.clearOptions();
        state.options.forEach(function (option) {
          var visible = active
            ? allowed.indexOf(option.value) !== -1
            : option.value !== 4 || state.initialStatus === 4;
          if (visible) control.addOption(option);
        });
        control.setDisabled(!active);
      });
      if (environmentMatches) {
        form.ui.setFormNotification(
          "Development test mode — " + (active
            ? "Maria only: dropdown simulation. Approval changes cannot be saved."
            : "Status is read-only. Simulation requires the configured account and Maria's Pending Superior request."),
          "WARNING", bannerId
        );
      } else {
        form.ui.clearFormNotification(bannerId);
      }
      if (status.getValue() === state.initialStatus) form.ui.clearFormNotification(saveId);
    }
    state.render = render;
    states.set(form, state);

    status.addOnChange(function () {
      if (!eligible() || allowed.indexOf(status.getValue()) === -1) {
        status.setValue(state.initialStatus);
      }
      render();
    });
    employee.addOnChange(render);
    form.data.entity.addOnSave(function (saveContext) {
      if (status.getValue() !== state.initialStatus) {
        saveContext.getEventArgs().preventDefault();
        form.ui.setFormNotification(
          "Dropdown test only: saving is blocked. Select Pending Superior again, or discard your changes.",
          "WARNING", saveId
        );
      }
    });
    // Refresh after a save or explicit data refresh without retaining stale eligibility.
    form.data.addOnLoad(function () {
      state.initialStatus = status.getValue();
      render();
    });
    render(); // Locked while the current user's identity is being verified.
    if (!environmentMatches) return;

    // userSettings.userId is a Dataverse systemuser ID, NOT an Entra Object ID.
    return Xrm.WebApi.retrieveRecord(
      "systemuser", guid(context.userSettings.userId),
      "?$select=azureactivedirectoryobjectid"
    ).then(function (user) {
      state.verified = guid(user.azureactivedirectoryobjectid) === TEST.managerEntraId;
      render();
    }).catch(function () {
      state.verified = false;
      render();
      form.ui.setFormNotification(
        "Development test mode — identity verification failed. Status remains read-only.",
        "ERROR", bannerId
      );
    });
  }

  return { onLoad: onLoad };
})();
