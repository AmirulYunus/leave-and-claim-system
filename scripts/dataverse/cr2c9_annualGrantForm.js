var AnnualGrantForm = (function () {
    "use strict";
    function onLoad(executionContext) {
        var form = executionContext.getFormContext();
        var control = form.getControl("WebResource_AnnualGrant");
        function initialize() {
            if (!control) return;
            control.getContentWindow().then(function (contentWindow) {
                contentWindow.setGrantContext(Xrm, form);
            }).catch(function () {
                form.ui.setFormNotification("Annual balance controls could not load. Refresh the record to retry.", "WARNING", "annual-grant-control");
            });
        }
        initialize();
        var tab = form.ui.tabs.get("annual_balance_tab");
        if (tab) tab.addTabStateChange(initialize);
        form.data.addOnLoad(initialize);
    }
    return { onLoad: onLoad };
}());
