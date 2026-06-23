/**
 * Minesweeper — classic puzzle game.
 * Left-click to reveal, right-click to flag. Clear all non-mine cells to win.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

export function registerMinesweeper(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/minesweeper.html";

  server.resource("Minesweeper UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(minesweeperHtml()),
    }],
  }));

  server.registerTool(
    "play_minesweeper",
    {
      description: "Play Minesweeper. Left-click to reveal cells, right-click to flag mines. Clear all safe cells to win!",
      inputSchema: {
        difficulty: z.enum(["easy", "medium", "hard"]).optional().describe("Difficulty (default: easy). Easy=9x9/10mines, Medium=16x16/40, Hard=16x30/99"),
      },
      annotations: { readOnlyHint: true },
      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const diff = args.difficulty || "easy";
      const configs = { easy: { w: 9, h: 9, mines: 10 }, medium: { w: 16, h: 16, mines: 40 }, hard: { w: 30, h: 16, mines: 99 } };
      return {
        content: [{ type: "text" as const, text: `Minesweeper (${diff}). Click to reveal, right-click to flag!` }],
        structuredContent: { difficulty: diff, ...configs[diff] },
      };
    }
  );
}

function minesweeperHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Minesweeper</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#c0c0c0; display:flex; flex-direction:column;
    align-items:center; padding:12px; }
  .hud { display:flex; gap:16px; margin-bottom:8px; font-size:14px; font-weight:700; color:#333; }
  #board { display:inline-grid; gap:1px; background:#808080; border:2px solid #808080; }
  .cell { width:24px; height:24px; display:flex; align-items:center; justify-content:center;
    font-size:12px; font-weight:700; cursor:pointer; user-select:none; background:#c0c0c0;
    border:2px outset #fff; }
  .cell.revealed { border:1px solid #808080; background:#d0d0d0; cursor:default; }
  .cell.mine { background:#ff4444; }
  .cell.flagged::after { content:"\\1F6A9"; }
  .c1{color:#0000ff} .c2{color:#008000} .c3{color:#ff0000} .c4{color:#000080}
  .c5{color:#800000} .c6{color:#008080} .c7{color:#000} .c8{color:#808080}
  .msg { margin-top:8px; font-size:14px; font-weight:700; }
</style>
</head>
<body>
<div class="hud">
  <span id="mineCount">Mines: 10</span>
  <span id="status">Playing</span>
</div>
<div id="board"></div>
<div class="msg" id="msg"></div>
<script>
(function(){
  var W=9,H=9,MINES=10,grid,revealed,flagged,mineSet,alive,firstClick;

  function init(w,h,m){
    W=w||9;H=h||9;MINES=m||10;
    grid=[];revealed=[];flagged=[];mineSet=new Set();alive=true;firstClick=true;
    for(var i=0;i<W*H;i++){grid.push(0);revealed.push(false);flagged.push(false);}
    document.getElementById("mineCount").textContent="Mines: "+MINES;
    document.getElementById("status").textContent="Playing";
    document.getElementById("msg").textContent="";
    render();
  }

  function placeMines(safeIdx){
    var placed=0;
    while(placed<MINES){
      var idx=Math.floor(Math.random()*W*H);
      if(idx===safeIdx||mineSet.has(idx)) continue;
      mineSet.add(idx); grid[idx]=-1; placed++;
    }
    for(var i=0;i<W*H;i++){
      if(grid[i]===-1) continue;
      var count=0;
      neighbors(i).forEach(function(n){if(grid[n]===-1)count++;});
      grid[i]=count;
    }
  }

  function neighbors(idx){
    var r=Math.floor(idx/W),c=idx%W,ns=[];
    for(var dr=-1;dr<=1;dr++) for(var dc=-1;dc<=1;dc++){
      if(dr===0&&dc===0) continue;
      var nr=r+dr,nc=c+dc;
      if(nr>=0&&nr<H&&nc>=0&&nc<W) ns.push(nr*W+nc);
    }
    return ns;
  }

  function reveal(idx){
    if(!alive||revealed[idx]||flagged[idx]) return;
    if(firstClick){firstClick=false;placeMines(idx);}
    revealed[idx]=true;
    if(grid[idx]===-1){alive=false;revealAll();document.getElementById("status").textContent="Game Over";
      document.getElementById("msg").textContent="Click to restart";return;}
    if(grid[idx]===0) neighbors(idx).forEach(reveal);
    checkWin();
  }

  function revealAll(){for(var i=0;i<W*H;i++) revealed[i]=true; render();}

  function checkWin(){
    var safe=0;for(var i=0;i<W*H;i++) if(revealed[i]&&grid[i]!==-1) safe++;
    if(safe===W*H-MINES){alive=false;document.getElementById("status").textContent="You Win!";
      document.getElementById("msg").textContent="Click to restart";}
    render();
  }

  function render(){
    var b=document.getElementById("board");
    b.style.gridTemplateColumns="repeat("+W+",24px)";
    b.innerHTML="";
    for(var i=0;i<W*H;i++){
      var d=document.createElement("div");
      d.className="cell";
      if(revealed[i]){
        d.classList.add("revealed");
        if(grid[i]===-1){d.classList.add("mine");d.textContent="\\u{1F4A3}";}
        else if(grid[i]>0){d.textContent=grid[i];d.classList.add("c"+grid[i]);}
      } else if(flagged[i]){d.classList.add("flagged");}
      (function(idx){
        d.addEventListener("click",function(){
          if(!alive){init(W,H,MINES);return;}
          reveal(idx);
        });
        d.addEventListener("contextmenu",function(e){
          e.preventDefault();
          if(!alive||revealed[idx]) return;
          flagged[idx]=!flagged[idx]; render();
        });
      })(i);
      b.appendChild(d);
    }
  }

  window.addEventListener("message",function(e){
    if(e.data&&e.data.structuredContent){
      var d=e.data.structuredContent;
      init(d.w||9,d.h||9,d.mines||10);
    }
  });
})();
<\/script>
</body>
</html>`;
}
