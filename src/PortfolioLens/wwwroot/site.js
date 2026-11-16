document.querySelectorAll("[data-question]").forEach(button => {
    button.addEventListener("click", () => {
        const question = document.querySelector("#Question");
        question.value = button.dataset.question;
        question.focus();
    });
});
document.querySelector("#query-form")?.addEventListener("submit", () => {
    document.querySelector("#ask-button").disabled = true;
    document.querySelector("#query-progress").hidden = false;
});
