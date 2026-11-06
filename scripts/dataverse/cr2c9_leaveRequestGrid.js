/* Superior app main-grid decisions. The server remains authoritative for
 * assignment, HR override, self-approval, category eligibility and accounting.
 * Configure SelectedControlSelectedItemIds and SelectedControl as parameters. */
var LeaveRequestGrid = (function () {
  "use strict";
  var busy = false;
  function id(value) { return String(value || "").replace(/[{}]/g, "").toLowerCase(); }
  function selected(values) {
    if (Array.isArray(values)) return values.map(id).filter(Boolean);
    if (typeof values === "string") return values.split(",").map(id).filter(Boolean);
    return [];
  }
  function errorText(error) {
    return (error && (error.message || error.description)) || "The decision was not saved. Refresh and try again.";
  }
  async function refresh(grid) {
    if (grid && typeof grid.refresh === "function") await grid.refresh();
  }
  async function decide(values, grid, outcome) {
    if (busy) return;
    var ids = selected(values);
    if (ids.length !== 1) {
      await Xrm.Navigation.openAlertDialog({ text: "Select exactly one pending leave request." });
      return;
    }
    busy = true;
    try {
      var row = await Xrm.WebApi.retrieveRecord("cr2c9_leaverequest", ids[0],
        "?$select=cr2c9_leaverequestnumber,cr2c9_status");
      if (row.cr2c9_status !== 0) throw new Error("This request is no longer Pending Superior. Refresh the list.");
      var version = /^W\/"(\d+)"$/.exec(String(row["@odata.etag"] || ""));
      if (!version) throw new Error("The request version could not be verified. Refresh the list.");
      var verb = outcome === 1 ? "Approve" : "Reject";
      var answer = await Xrm.Navigation.openConfirmDialog({
        title: verb + " leave request",
        text: verb + " " + (row.cr2c9_leaverequestnumber || "this leave request") + "?",
        confirmButtonLabel: verb,
        cancelButtonLabel: "Back"
      });
      if (!answer.confirmed) return;
      await Xrm.WebApi.updateRecord("cr2c9_leaverequest", ids[0], {
        cr2c9_status: outcome,
        cr2c9_decisionexpectedversion: version[1]
      });
      var message = "Leave request " + (outcome === 1 ? "approved" : "rejected") + ".";
      try { await refresh(grid); }
      catch (_) { message += " Refresh the list to see the new status."; }
      await Xrm.Navigation.openAlertDialog({ text: message });
    } catch (error) {
      await Xrm.Navigation.openAlertDialog({ text: errorText(error), title: "Decision not saved" });
      try { await refresh(grid); } catch (_) { /* Keep the original error visible. */ }
    } finally { busy = false; }
  }
  return {
    approve: function (values, grid) { return decide(values, grid, 1); },
    reject: function (values, grid) { return decide(values, grid, 3); }
  };
})();
