'use strict';

var BulsatcomConfig = {
    pluginUniqueId: 'f996e2e1-3335-4b39-adf2-417d38b18b6d'
};

function onViewShow() {
    Dashboard.showLoadingMsg();
    var page = this;

    ApiClient.getPluginConfiguration(BulsatcomConfig.pluginUniqueId).then(function (config) {
        page.querySelector("#Username").value = config.Username || '';
        page.querySelector("#Password").value = config.Password || '';
        page.querySelector("#M3uFileName").value = config.M3uFileName || 'bulsatcom.m3u';
        page.querySelector("#EpgFileName").value = config.EpgFileName || 'bulsatcom.xml';
        page.querySelector("#EnableScheduledTask").checked = config.EnableScheduledTask || false;
        page.querySelector("#UpdateIntervalHours").value = config.UpdateIntervalHours || 6;

        Dashboard.hideLoadingMsg();
    });
}

function onSubmit(e) {
    Dashboard.showLoadingMsg();
    var form = this;

    ApiClient.getPluginConfiguration(BulsatcomConfig.pluginUniqueId).then(function (config) {
        config.Username = form.querySelector('#Username').value;
        config.Password = form.querySelector('#Password').value;
        config.M3uFileName = form.querySelector('#M3uFileName').value || 'bulsatcom.m3u';
        config.EpgFileName = form.querySelector('#EpgFileName').value || 'bulsatcom.xml';
        config.EnableScheduledTask = form.querySelector('#EnableScheduledTask').checked;
        config.UpdateIntervalHours = parseInt(form.querySelector('#UpdateIntervalHours').value) || 6;

        ApiClient.updatePluginConfiguration(BulsatcomConfig.pluginUniqueId, config).then(function (result) {
            Dashboard.processServerConfigurationUpdateResult(result);
        });
    });

    e.preventDefault();
    return false;
}

export default function (view, params) {
    view.querySelector('form').addEventListener('submit', onSubmit);
    view.addEventListener('viewshow', onViewShow);
}