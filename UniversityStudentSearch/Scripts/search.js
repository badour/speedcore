(function () {
    var field = document.getElementById("ddlField");
    var input = document.getElementById("txtTerm");
    var hint = document.getElementById("searchHint");
    if (!field || !input) {
        return;
    }

    var copy = {
        id: {
            placeholder: "Student ID",
            hint: "Matches the student ID exactly.",
            mode: "numeric"
        },
        name: {
            placeholder: "Full or partial name",
            hint: "Matches any part of the student name.",
            mode: "search"
        },
        stage: {
            placeholder: "Stage",
            hint: "Matches any part of the stage.",
            mode: "search"
        },
        department: {
            placeholder: "Department",
            hint: "Matches any part of the department.",
            mode: "search"
        }
    };

    function sync() {
        var item = copy[field.value] || copy.id;
        input.placeholder = item.placeholder;
        input.setAttribute("inputmode", item.mode);
        input.setAttribute("spellcheck", item.mode === "numeric" ? "false" : "true");
        if (hint) {
            hint.textContent = item.hint;
        }
    }

    field.addEventListener("change", sync);
    sync();
})();
