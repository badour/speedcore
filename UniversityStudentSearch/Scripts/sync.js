(function () {
    document.addEventListener("change", function (event) {
        var target = event.target;
        if (!target || !target.classList || !target.classList.contains("pick-all")) {
            return;
        }

        var boxes = document.querySelectorAll("input.pick");
        for (var i = 0; i < boxes.length; i++) {
            boxes[i].checked = target.checked;
        }
    });
})();
