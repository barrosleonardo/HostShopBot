"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/notificationHub").build();

connection.on("ReceiveNotification", function (message) {
    var notification = document.createElement("div");
    notification.className = "alert alert-info alert-dismissible fade show";
    notification.role = "alert";
    notification.innerHTML = `${message} <button type="button" class="close" data-dismiss="alert" aria-label="Close"><span aria-hidden="true">&times;</span></button>`;
    document.body.insertBefore(notification, document.body.firstChild);
});

connection.start().then(function () {
    console.log("SignalR conectado.");
}).catch(function (err) {
    return console.error(err.toString());
});