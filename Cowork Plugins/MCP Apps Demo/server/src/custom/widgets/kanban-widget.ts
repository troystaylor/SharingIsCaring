/**
 * Kanban Board widget — self-contained HTML with inline CSS/JS.
 * Renders cards in columns, supports drag-to-move via widget tools/call to move_card.
 */

import { injectDisclaimer } from "../../shared/disclaimer.js";

export function kanbanWidgetHtml(): string {
  const html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Kanban Board</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,-apple-system,sans-serif; background:#f0f2f5; padding:16px; padding-bottom:48px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:12px; }
  .board { display:flex; gap:12px; overflow-x:auto; min-height:400px; }
  .column { flex:1; min-width:220px; background:#fff; border-radius:8px; padding:12px;
    border:2px solid transparent; transition:border-color 0.2s; }
  .column.drag-over { border-color:#4361ee; background:#f0f4ff; }
  .column-header { font-size:14px; font-weight:600; color:#555; margin-bottom:8px;
    display:flex; justify-content:space-between; align-items:center; }
  .column-header .count { background:#e0e0e0; border-radius:10px; padding:2px 8px;
    font-size:12px; font-weight:500; }
  .card { background:#fff; border:1px solid #e0e0e0; border-radius:6px; padding:10px;
    margin-bottom:8px; cursor:grab; transition:box-shadow 0.2s, transform 0.2s; }
  .card:hover { box-shadow:0 2px 8px rgba(0,0,0,0.1); }
  .card.dragging { opacity:0.5; transform:rotate(2deg); }
  .card-id { font-size:11px; color:#888; font-weight:500; }
  .card-title { font-size:13px; font-weight:600; color:#1a1a2e; margin:4px 0; }
  .card-meta { display:flex; justify-content:space-between; align-items:center; margin-top:6px; }
  .card-assignee { font-size:11px; color:#666; }
  .priority { font-size:10px; font-weight:600; padding:2px 6px; border-radius:4px; text-transform:uppercase; }
  .priority.high { background:#ffe0e0; color:#d32f2f; }
  .priority.medium { background:#fff3e0; color:#f57c00; }
  .priority.low { background:#e8f5e9; color:#388e3c; }
</style>
</head>
<body>
<h1 id="board-title">Kanban Board</h1>
<div class="board" id="board"></div>

<script>
(function() {
  const COLUMNS = ["To Do", "In Progress", "Done"];
  let boardData = null;

  // Listen for data from bootstrap bridge
  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) {
      boardData = e.data.structuredContent;
      render();
    }
  });

  function render() {
    if (!boardData) return;
    const titleEl = document.getElementById("board-title");
    titleEl.textContent = boardData.project || "Kanban Board";

    const boardEl = document.getElementById("board");
    boardEl.innerHTML = "";

    for (const col of boardData.columns || COLUMNS) {
      const cards = (boardData.board && boardData.board[col]) || [];
      const colEl = document.createElement("div");
      colEl.className = "column";
      colEl.dataset.column = col;

      colEl.innerHTML = '<div class="column-header"><span>' + escHtml(col) +
        '</span><span class="count">' + cards.length + '</span></div>';

      for (const card of cards) {
        const cardEl = document.createElement("div");
        cardEl.className = "card";
        cardEl.draggable = true;
        cardEl.dataset.cardId = card.id;
        cardEl.innerHTML =
          '<div class="card-id">' + escHtml(card.id) + '</div>' +
          '<div class="card-title">' + escHtml(card.title) + '</div>' +
          '<div class="card-meta">' +
            '<span class="card-assignee">' + escHtml(card.assignee) + '</span>' +
            '<span class="priority ' + card.priority + '">' + card.priority + '</span>' +
          '</div>';

        cardEl.addEventListener("dragstart", (e) => {
          e.dataTransfer.setData("text/plain", card.id);
          cardEl.classList.add("dragging");
        });
        cardEl.addEventListener("dragend", () => cardEl.classList.remove("dragging"));
        colEl.appendChild(cardEl);
      }

      // Drop zone
      colEl.addEventListener("dragover", (e) => { e.preventDefault(); colEl.classList.add("drag-over"); });
      colEl.addEventListener("dragleave", () => colEl.classList.remove("drag-over"));
      colEl.addEventListener("drop", async (e) => {
        e.preventDefault();
        colEl.classList.remove("drag-over");
        const cardId = e.dataTransfer.getData("text/plain");
        if (!cardId) return;
        // Call move_card via bootstrap's tool call bridge
        if (window.__mcpCallTool) {
          try {
            var result = await window.__mcpCallTool("move_card", { cardId: cardId, targetColumn: col });
            // Result contains updated board in structuredContent
            if (result && result.structuredContent) {
              boardData = result.structuredContent;
              render();
            }
          } catch (err) {
            console.error("move_card failed:", err);
          }
        }
      });

      boardEl.appendChild(colEl);
    }
  }

  function escHtml(s) {
    const d = document.createElement("div");
    d.textContent = s || "";
    return d.innerHTML;
  }
})();
</script>
</body>
</html>`;

  return injectDisclaimer(html);
}
