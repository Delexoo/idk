const scenarios = [
  {
    id: "email",
    label: "Email",
    tutorialHighlight: "University of California, Santa Barbara",
    tutorialAsk: "What's the acceptance rate?",
    html: `
      <div class="demo-email">
        <article class="demo-email__window">
          <header class="demo-email__titlebar">
            <svg class="demo-email__title-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <path d="M4 6.5A2.5 2.5 0 0 1 6.5 4h11A2.5 2.5 0 0 1 20 6.5v11a2.5 2.5 0 0 1-2.5 2.5h-11A2.5 2.5 0 0 1 4 17.5v-11Z" stroke="currentColor" stroke-width="1.5"/>
              <path d="m5 8 7 6 7-6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            <span class="demo-email__title">Congratulations on your interest in our program</span>
            <div class="demo-email__winbtns" aria-hidden="true">
              <span></span><span></span><span></span>
            </div>
          </header>
          <div class="demo-email__fields">
            <div class="demo-email__field">
              <span class="demo-email__label">From</span>
              <span class="demo-email__value">UCSB Admissions &lt;admissions@example.edu&gt;</span>
            </div>
            <div class="demo-email__field">
              <span class="demo-email__label">To</span>
              <span class="demo-email__value">Alex Chen &lt;alex.chen@example.com&gt;</span>
            </div>
            <div class="demo-email__field demo-email__field--subject">
              <span class="demo-email__label">Subject</span>
              <span class="demo-email__value">Congratulations on your interest in our program</span>
            </div>
          </div>
          <div class="demo-email__read-meta">
            <time datetime="2025-05-06T09:42">Tue, May 6, 2025, 9:42 AM</time>
          </div>
          <div class="demo-email__message">
            <p>Dear Alex,</p>
            <p>Congratulations on your interest in our program. Our main campus is at the
            <span class="demo-selectable" data-phrase="University of California, Santa Barbara">University of California, Santa Barbara</span>,
            with visits available year-round.</p>
            <p>Best regards,<br>UCSB Admissions Office</p>
          </div>
          <footer class="demo-email__actions">
            <button type="button" class="demo-email__reply" tabindex="-1">Reply</button>
            <div class="demo-email__action-icons" aria-hidden="true">
              <span title="Reply all"></span>
              <span title="Forward"></span>
              <span title="Delete"></span>
            </div>
          </footer>
        </article>
      </div>
    `,
    answer:
      "UC Santa Barbara (UCSB) has an overall acceptance rate of approximately 38%. It is a highly competitive, public research university that receives over 110,000 applications annually, with admission standards that can vary significantly depending on the specific major and residency status.",
    answerShort:
      "UCSB’s overall acceptance rate is about 38%, though it varies by major and residency.",
  },
  {
    id: "article",
    label: "Article",
    tutorialHighlight: "glucuronolactone",
    tutorialAsk: "What is this?",
    html: `
      <div class="demo-article">
        <article class="demo-article__window">
          <header class="demo-article__titlebar">
            <svg class="demo-article__title-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <path d="M6 4h9l5 5v11a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z" stroke="currentColor" stroke-width="1.5"/>
              <path d="M14 4v5h5" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/>
              <path d="M8 12h8M8 15h6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            </svg>
            <span class="demo-article__title">Energy drink ingredients</span>
            <div class="demo-article__winbtns" aria-hidden="true">
              <span></span><span></span><span></span>
            </div>
          </header>
          <div class="demo-article__meta">
            <span class="demo-article__site">healthline.com</span>
            <span class="demo-article__sep">·</span>
            <span>4 min read</span>
            <span class="demo-article__sep">·</span>
            <span>May 6, 2025</span>
          </div>
          <div class="demo-article__body">
            <p>Many formulas list caffeine, taurine, and
            <span class="demo-selectable" data-phrase="glucuronolactone">glucuronolactone</span>
            on the label. The last one sounds technical; brands use it to signal a “science-backed” formula even when the effect is mostly marketing.</p>
          </div>
          <footer class="demo-article__actions">
            <button type="button" class="demo-article__primary" tabindex="-1">Share</button>
            <div class="demo-article__action-icons" aria-hidden="true">
              <span title="Save"></span>
              <span title="Print"></span>
            </div>
          </footer>
        </article>
      </div>
    `,
    answer:
      "Glucuronolactone is a carbohydrate metabolite sometimes added to energy drinks. It is often paired with caffeine and taurine in product marketing.",
    answerShort:
      "A carbohydrate metabolite often added to energy drinks, usually for marketing.",
  },
  {
    id: "chat",
    label: "Chat",
    tutorialHighlight: "What kind of summer activities should we do?",
    tutorialAsk: "Give me Ideas!",
    html: `
      <div class="demo-chat">
        <article class="demo-chat__window">
          <header class="demo-chat__titlebar">
            <svg class="demo-chat__title-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <path d="M4 6.5A3.5 3.5 0 0 1 7.5 3h9A3.5 3.5 0 0 1 20 6.5v7A3.5 3.5 0 0 1 16.5 17h-5.2L8 20.5V17H7.5A3.5 3.5 0 0 1 4 13.5v-7Z" stroke="currentColor" stroke-width="1.5"/>
            </svg>
            <span class="demo-chat__title">Team chat</span>
            <div class="demo-chat__winbtns" aria-hidden="true">
              <span></span><span></span><span></span>
            </div>
          </header>
          <div class="demo-chat__thread">
            <div class="demo-chat__bubble demo-chat__bubble--them">
              <span class="demo-chat__sender">Jane</span>
              <p><span class="demo-selectable" data-phrase="What kind of summer activities should we do?">What kind of summer activities should we do?</span></p>
            </div>
            <div class="demo-chat__bubble demo-chat__bubble--me">
              <span class="demo-chat__sender">You</span>
              <p>Good question — let me think.</p>
            </div>
          </div>
          <footer class="demo-chat__composer">
            <div class="demo-chat__input" aria-hidden="true">Message…</div>
            <button type="button" class="demo-chat__send" tabindex="-1" aria-label="Send">Send</button>
          </footer>
        </article>
      </div>
    `,
    answer:
      "Ideas: weekend beach trips, a nearby hike, farmers markets, outdoor movie nights, a short road trip, or a low-key cook-together night. Mix one active day with one lazy day.",
    answerShort:
      "Beach trips, a hike, farmers markets, outdoor movies, or a short road trip.",
  },
];

let activeIndex = 0;
let selectedPhrase = "";
const TUTORIAL_START_DELAY_MS = 3000;

let tutorialPhase = "idle"; // idle | intro | ask | answer | done
let tutorialStartTimer = null;

const stage = document.getElementById("demo-stage");
const docEl = document.getElementById("demo-doc");
const tabsEl = document.getElementById("demo-tabs");
const pillEl = document.getElementById("demo-pill");
const pillInput = document.getElementById("demo-pill-input");
const PILL_W = 128;
const PILL_H = 31;
const answerEl = document.getElementById("demo-answer");
const cardMessagesEl = document.getElementById("demo-card-messages");
const followUpInput = document.getElementById("demo-followup-input");
const cardCloseBtn = document.getElementById("demo-card-close");
const cardSimplerBtn = document.getElementById("demo-card-simpler");
const tutorialEl = document.getElementById("demo-tutorial");
const tutorialMsg = document.getElementById("demo-tutorial-msg");

let spotlightTarget = null;
let lockedScrollY = 0;

function activeScenario() {
  return scenarios[activeIndex];
}

function lockPageScroll() {
  if (document.body.classList.contains("tutorial-scroll-lock")) return;
  lockedScrollY = window.scrollY;
  document.body.classList.add("tutorial-scroll-lock");
  document.body.style.top = `-${lockedScrollY}px`;
}

function unlockPageScroll() {
  if (!document.body.classList.contains("tutorial-scroll-lock")) return;
  document.body.classList.remove("tutorial-scroll-lock");
  document.body.style.top = "";
  window.scrollTo(0, lockedScrollY);
}

function isTutorialActive() {
  return tutorialPhase !== "idle" && tutorialPhase !== "done";
}

function clearTutorialTimer() {
  if (tutorialStartTimer) {
    clearTimeout(tutorialStartTimer);
    tutorialStartTimer = null;
  }
}

function renderTabs() {
  tabsEl.innerHTML = scenarios
    .map(
      (s, i) =>
        `<button type="button" class="demo-tab${i === activeIndex ? " is-active" : ""}" data-index="${i}">${s.label}</button>`
    )
    .join("");
  tabsEl.querySelectorAll(".demo-tab").forEach((btn) => {
    btn.addEventListener("click", () => {
      if (isTutorialActive()) return;
      const newIndex = Number(btn.dataset.index);
      if (newIndex === activeIndex) return;
      switchScenario(newIndex);
    });
  });
}

function renderDoc() {
  const s = activeScenario();
  docEl.classList.toggle(
    "demo-doc--window",
    s.id === "email" || s.id === "article" || s.id === "chat"
  );
  docEl.innerHTML = s.html;
  docEl.querySelectorAll(".demo-selectable").forEach((el) => {
    el.addEventListener("mouseup", onTextSelected);
  });
  const target = docEl.querySelector(".demo-selectable");
  target?.classList.add("demo-tutorial-target");
}

function getTutorialTarget() {
  return docEl.querySelector(".demo-tutorial-target");
}

function setTutorialMessage(html) {
  tutorialMsg.innerHTML = html;
}

function updateSpotlight(el, padding = 6) {
  if (!el || tutorialEl.classList.contains("is-hidden")) {
    tutorialEl.style.clipPath = "";
    spotlightTarget = null;
    return;
  }

  spotlightTarget = el;
  const r = el.getBoundingClientRect();
  const x = Math.max(0, r.left - padding);
  const y = Math.max(0, r.top - padding);
  const w = r.width + padding * 2;
  const h = r.height + padding * 2;
  const W = window.innerWidth;
  const H = window.innerHeight;

  tutorialEl.style.clipPath = `polygon(
    evenodd,
    0 0, ${W}px 0, ${W}px ${H}px, 0 ${H}px, 0 0,
    ${x}px ${y}px,
    ${x + w}px ${y}px,
    ${x + w}px ${y + h}px,
    ${x}px ${y + h}px,
    ${x}px ${y}px
  )`;
}

function showTutorialOverlay(passThrough = false) {
  tutorialEl.classList.remove("is-hidden");
  tutorialEl.classList.add("is-faded-in");
  tutorialEl.classList.toggle("demo-tutorial--pass-through", passThrough);
  document.body.classList.add("tutorial-overlay-on");
}

function hideTutorialOverlay() {
  tutorialEl.classList.add("is-hidden");
  tutorialEl.classList.remove("demo-tutorial--pass-through", "is-faded-in");
  tutorialEl.style.clipPath = "";
  tutorialEl.style.cursor = "";
  document.body.classList.remove("tutorial-overlay-on");
  spotlightTarget = null;
}

function clearTutorialHighlights() {
  docEl.querySelectorAll(".demo-tutorial-target").forEach((n) => {
    n.classList.remove("tutorial-spotlight-text", "tutorial-pulse", "is-selected");
  });
  pillEl.classList.remove("tutorial-spotlight-pill");
  answerEl.classList.remove("tutorial-spotlight-answer");
}

function resetTutorialUi() {
  clearTutorialTimer();
  clearTutorialHighlights();
  hideTutorialOverlay();
  hidePill();
  hideAnswer();
  unlockPageScroll();
  tutorialPhase = "idle";
  selectedPhrase = "";
  window.getSelection()?.removeAllRanges();
}

function finishTutorial() {
  tutorialPhase = "done";
  clearTutorialHighlights();
  hideTutorialOverlay();
  unlockPageScroll();
}

function beginTutorialFadeIn() {
  tutorialPhase = "intro";
  lockPageScroll();

  const target = getTutorialTarget();
  target?.classList.add("tutorial-spotlight-text");

  const phrase = escapeHtml(activeScenario().tutorialHighlight);
  setTutorialMessage(`Highlight the word <strong>"${phrase}"</strong>`);

  tutorialEl.classList.remove("is-hidden");
  tutorialEl.classList.add("demo-tutorial--pass-through");
  document.body.classList.add("tutorial-overlay-on");

  requestAnimationFrame(() => {
    updateSpotlight(target, 8);
    requestAnimationFrame(() => {
      tutorialEl.classList.add("is-faded-in");
    });
  });

  tutorialEl.addEventListener(
    "transitionend",
    (e) => {
      if (e.target !== tutorialEl || e.propertyName !== "opacity") return;
      updateSpotlight(target, 8);
    },
    { once: true }
  );
}

function scheduleTutorialStart(delay = TUTORIAL_START_DELAY_MS) {
  clearTutorialTimer();
  tutorialStartTimer = setTimeout(() => {
    tutorialStartTimer = null;
    beginTutorialFadeIn();
  }, delay);
}

function switchScenario(index) {
  resetTutorialUi();
  activeIndex = index;
  renderTabs();
  renderDoc();
  scheduleTutorialStart();
}

function startAskPhase() {
  tutorialPhase = "ask";
  clearTutorialHighlights();

  pillEl.classList.add("tutorial-spotlight-pill");
  const ask = escapeHtml(activeScenario().tutorialAsk);
  setTutorialMessage(`Type <strong>"${ask}"</strong>`);
  showTutorialOverlay();

  requestAnimationFrame(() => {
    updateSpotlight(pillEl, 10);
    pillInput.focus();
  });
}

function startAnswerPhase() {
  tutorialPhase = "answer";
  clearTutorialHighlights();

  answerEl.classList.add("tutorial-spotlight-answer");
  setTutorialMessage(
    'This is where you get your answer<br><span class="demo-tutorial__sub">Press Esc to continue</span>'
  );
  showTutorialOverlay();
  tutorialEl.style.cursor = "pointer";

  requestAnimationFrame(() => updateSpotlight(answerEl, 14));
}

function onTextSelected() {
  if (tutorialPhase === "idle") return;

  const sel = window.getSelection();
  if (!sel || sel.isCollapsed) return;
  const text = sel.toString().trim();
  const el = sel.anchorNode?.parentElement?.closest?.(".demo-selectable");
  if (!el || !text) return;

  const target = getTutorialTarget();
  if (tutorialPhase === "intro") {
    if (el !== target) return;
    hideTutorialOverlay();
    target.classList.remove("tutorial-spotlight-text");
  }

  selectedPhrase = text;
  docEl.querySelectorAll(".demo-selectable").forEach((n) => {
    n.classList.remove("is-selected", "tutorial-pulse");
  });
  el.classList.add("is-selected");

  const rect = el.getBoundingClientRect();
  const stageRect = stage.getBoundingClientRect();
  const left = Math.min(
    stageRect.width - PILL_W - 12,
    Math.max(12, rect.left - stageRect.left + rect.width / 2 - PILL_W / 2)
  );
  const top = Math.min(
    stageRect.height - PILL_H - 12,
    Math.max(12, rect.bottom - stageRect.top + 10)
  );

  pillEl.style.left = `${left}px`;
  pillEl.style.top = `${top}px`;
  pillEl.classList.add("is-visible");
  pillInput.value = "";
  pillInput.placeholder = "Ask Anything!";
  hideAnswer();

  if (tutorialPhase === "intro") {
    requestAnimationFrame(() => startAskPhase());
  } else if (tutorialPhase !== "ask") {
    pillInput.focus();
  }
}

function hidePill() {
  if (tutorialPhase === "ask") return;
  pillEl.classList.remove("is-visible");
}

function escapeHtml(text) {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function formatAnswerHtml(text) {
  const escaped = escapeHtml(text.trim());
  const paragraphs = escaped.split(/\n\n+/).filter(Boolean);
  if (paragraphs.length === 0) return "<p></p>";
  return paragraphs
    .map((p) => `<p>${p.replace(/\n/g, "<br />")}</p>`)
    .join("");
}

function createUserBubbleHtml(question) {
  return `
    <div class="demo-chat-row demo-chat-row--user">
      <div class="demo-chat-bubble demo-chat-bubble--user">${escapeHtml(question)}</div>
    </div>`;
}

function createAssistantTypingHtml() {
  return `
    <div class="demo-chat-row demo-chat-row--assistant">
      <div class="demo-chat-avatar"><img src="assets/idk.png" width="26" height="26" alt="" /></div>
      <div class="demo-chat-assistant-col">
        <p class="demo-chat-label">idk</p>
        <div class="demo-chat-bubble demo-chat-bubble--assistant">
          <div class="demo-chat-dots" aria-hidden="true"><span></span><span></span><span></span></div>
        </div>
      </div>
    </div>`;
}

function createAssistantBubbleHtml(answer) {
  return `
    <div class="demo-chat-row demo-chat-row--assistant">
      <div class="demo-chat-avatar"><img src="assets/idk.png" width="26" height="26" alt="" /></div>
      <div class="demo-chat-assistant-col">
        <p class="demo-chat-label">idk</p>
        <div class="demo-chat-bubble demo-chat-bubble--assistant">${formatAnswerHtml(answer)}</div>
      </div>
    </div>`;
}

function hideAnswer() {
  answerEl.classList.remove("is-visible");
  cardMessagesEl.innerHTML = "";
  if (followUpInput) {
    followUpInput.value = "";
    followUpInput.disabled = true;
  }
}

function normalizeTutorialInput(text) {
  return text
    .trim()
    .toLowerCase()
    .replace(/[''`´]/g, "")
    .replace(/[^a-z0-9\s]/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

function tutorialAskMatches(input) {
  const typed = normalizeTutorialInput(input);
  const expected = normalizeTutorialInput(activeScenario().tutorialAsk);
  return typed.length > 0 && typed === expected;
}

function showAnswer() {
  const scenario = activeScenario();
  if (tutorialPhase === "ask" && !tutorialAskMatches(pillInput.value)) return;

  const question = pillInput.value.trim();
  if (!question) return;

  pillEl.classList.remove("is-visible", "tutorial-spotlight-pill");
  window.getSelection()?.removeAllRanges();

  answerEl.classList.add("is-visible");
  followUpInput.disabled = true;
  followUpInput.value = "";

  cardMessagesEl.innerHTML =
    createUserBubbleHtml(question) + createAssistantTypingHtml();
  cardMessagesEl.scrollTop = cardMessagesEl.scrollHeight;

  const onAnswerReady = () => {
    const typingRow = cardMessagesEl.querySelector(".demo-chat-row--assistant");
    if (typingRow) typingRow.outerHTML = createAssistantBubbleHtml(scenario.answer);
    followUpInput.disabled = false;
    cardMessagesEl.scrollTop = cardMessagesEl.scrollHeight;

    if (tutorialPhase === "ask") {
      hideTutorialOverlay();
      requestAnimationFrame(() => startAnswerPhase());
    }
  };

  setTimeout(onAnswerReady, 750);
}

pillInput.addEventListener("keydown", (e) => {
  if (e.key !== "Enter") return;
  if (tutorialPhase === "ask" && !tutorialAskMatches(pillInput.value)) {
    e.preventDefault();
    pillEl.classList.remove("demo-pill--mismatch");
    void pillEl.offsetWidth;
    pillEl.classList.add("demo-pill--mismatch");
    return;
  }
  showAnswer();
});

pillInput.addEventListener("input", () => {
  pillEl.classList.remove("demo-pill--mismatch");
  if (tutorialPhase === "ask") {
    requestAnimationFrame(() => updateSpotlight(pillEl, 10));
  }
});

stage.addEventListener("mousedown", (e) => {
  if (tutorialPhase === "idle" || tutorialPhase === "ask" || tutorialPhase === "intro")
    return;
  if (
    e.target.closest(".demo-pill") ||
    e.target.closest(".demo-selectable") ||
    e.target.closest(".demo-answer-card")
  )
    return;
  hidePill();
});

cardCloseBtn?.addEventListener("click", () => {
  if (tutorialPhase === "answer") finishTutorial();
  else {
    hideAnswer();
    hidePill();
  }
});

cardSimplerBtn?.addEventListener("click", () => {
  const assistantBubble = cardMessagesEl.querySelector(
    ".demo-chat-row--assistant:last-child .demo-chat-bubble--assistant"
  );
  if (!assistantBubble) return;
  assistantBubble.innerHTML = `<p>${escapeHtml(activeScenario().answerShort)}</p>`;
});

function spotlightPaddingFor(el) {
  if (el === pillEl) return 10;
  if (el === answerEl) return 14;
  return 8;
}

window.addEventListener("resize", () => {
  if (spotlightTarget && !tutorialEl.classList.contains("is-hidden")) {
    updateSpotlight(spotlightTarget, spotlightPaddingFor(spotlightTarget));
  }
});

window.addEventListener("scroll", () => {
  if (spotlightTarget && !tutorialEl.classList.contains("is-hidden")) {
    updateSpotlight(spotlightTarget, spotlightPaddingFor(spotlightTarget));
  }
}, true);

tutorialEl.addEventListener("click", () => {
  if (tutorialPhase === "answer") finishTutorial();
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape" && tutorialPhase === "answer") {
    e.preventDefault();
    finishTutorial();
  }
});

activeIndex = 0;
renderTabs();
renderDoc();
scheduleTutorialStart();
