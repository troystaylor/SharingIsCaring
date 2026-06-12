/**
 * 2048 Game — slide tiles to combine matching numbers.
 * Arrow keys or swipe to move. Reach 2048 to win!
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

export function register2048(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/2048.html";

  server.resource("2048 Game UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(game2048Html()),
    }],
  }));

  server.registerTool(
    "play_2048",
    {
      description: "Play the 2048 puzzle game. Use arrow keys to slide tiles — matching numbers combine. Reach 2048 to win!",
      inputSchema: {
        gridSize: z.number().min(3).max(6).optional().describe("Grid size (3-6, default 4)"),
      },
      annotations: { readOnlyHint: true },
      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const size = args.gridSize || 4;
      return {
        content: [{ type: "text" as const, text: `2048 game launched (${size}x${size} grid). Arrow keys to play!` }],
        structuredContent: { gridSize: size },
      };
    }
  );
}

function game2048Html(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>2048</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#faf8ef; display:flex; flex-direction:column;
    align-items:center; padding:16px; }
  h1 { font-size:36px; font-weight:800; color:#776e65; margin-bottom:4px; }
  .hud { display:flex; gap:12px; margin-bottom:12px; }
  .hud-box { background:#bbada0; border-radius:6px; padding:6px 16px; text-align:center; }
  .hud-label { font-size:10px; color:#eee4da; text-transform:uppercase; }
  .hud-value { font-size:18px; font-weight:700; color:#fff; }
  #board { display:grid; gap:8px; background:#bbada0; border-radius:8px; padding:8px; }
  .cell { border-radius:4px; display:flex; align-items:center; justify-content:center;
    font-weight:700; transition:all 0.1s; }
  .msg { margin-top:12px; font-size:13px; color:#776e65; }
</style>
</head>
<body>
<h1>2048</h1>
<div class="hud">
  <div class="hud-box"><div class="hud-label">Score</div><div class="hud-value" id="score">0</div></div>
  <div class="hud-box"><div class="hud-label">Best</div><div class="hud-value" id="best">0</div></div>
</div>
<div id="board"></div>
<div class="msg" id="msg">Arrow keys or swipe to play</div>
<script>
(function(){
  var N=4,grid,score=0,best=0,board=document.getElementById("board");
  var COLORS={0:"#cdc1b4",2:"#eee4da",4:"#ede0c8",8:"#f2b179",16:"#f59563",32:"#f67c5f",
    64:"#f65e3b",128:"#edcf72",256:"#edcc61",512:"#edc850",1024:"#edc53f",2048:"#edc22e"};
  var TEXTC={0:"transparent",2:"#776e65",4:"#776e65"};

  function init(size){
    N=size||4; grid=[]; score=0;
    board.style.gridTemplateColumns="repeat("+N+",1fr)";
    var cellSize=Math.floor(Math.min(360,window.innerWidth-48)/N);
    for(var i=0;i<N*N;i++) grid.push(0);
    addRandom(); addRandom(); render();
  }

  function addRandom(){
    var empty=[];
    for(var i=0;i<grid.length;i++) if(grid[i]===0) empty.push(i);
    if(empty.length===0) return;
    grid[empty[Math.floor(Math.random()*empty.length)]]=Math.random()<0.9?2:4;
  }

  function render(){
    var cellSize=Math.floor(Math.min(360,window.innerWidth-48)/N);
    board.innerHTML="";
    for(var i=0;i<grid.length;i++){
      var v=grid[i];
      var d=document.createElement("div");
      d.className="cell";
      d.style.width=d.style.height=cellSize+"px";
      d.style.fontSize=(v>=1024?Math.floor(cellSize/4):v>=128?Math.floor(cellSize/3.5):Math.floor(cellSize/3))+"px";
      d.style.background=COLORS[v]||"#3c3a32";
      d.style.color=TEXTC[v]||"#f9f6f2";
      d.textContent=v||"";
      board.appendChild(d);
    }
    document.getElementById("score").textContent=score;
    if(score>best){best=score;document.getElementById("best").textContent=best;}
  }

  function slide(row){
    var a=row.filter(function(x){return x!==0;});
    for(var i=0;i<a.length-1;i++){
      if(a[i]===a[i+1]){a[i]*=2;score+=a[i];a.splice(i+1,1);}
    }
    while(a.length<N) a.push(0);
    return a;
  }

  function move(dir){
    var moved=false,old=grid.slice();
    if(dir==="left"||dir==="right"){
      for(var r=0;r<N;r++){
        var row=grid.slice(r*N,r*N+N);
        if(dir==="right") row.reverse();
        row=slide(row);
        if(dir==="right") row.reverse();
        for(var c=0;c<N;c++) grid[r*N+c]=row[c];
      }
    } else {
      for(var c2=0;c2<N;c2++){
        var col=[];for(var r2=0;r2<N;r2++) col.push(grid[r2*N+c2]);
        if(dir==="down") col.reverse();
        col=slide(col);
        if(dir==="down") col.reverse();
        for(var r3=0;r3<N;r3++) grid[r3*N+c2]=col[r3];
      }
    }
    for(var i=0;i<grid.length;i++) if(grid[i]!==old[i]){moved=true;break;}
    if(moved){addRandom();render();}
    if(grid.indexOf(2048)!==-1){document.getElementById("msg").textContent="You win! 🎉";}
    else if(!canMove()){document.getElementById("msg").textContent="Game over! Refresh to retry.";}
  }

  function canMove(){
    for(var i=0;i<grid.length;i++){
      if(grid[i]===0) return true;
      if(i%N<N-1&&grid[i]===grid[i+1]) return true;
      if(i+N<grid.length&&grid[i]===grid[i+N]) return true;
    }
    return false;
  }

  document.addEventListener("keydown",function(e){
    if(e.key==="ArrowLeft") move("left");
    else if(e.key==="ArrowRight") move("right");
    else if(e.key==="ArrowUp") move("up");
    else if(e.key==="ArrowDown") move("down");
    if(e.key.startsWith("Arrow")) e.preventDefault();
  });

  var tx=0,ty=0;
  document.addEventListener("touchstart",function(e){tx=e.touches[0].clientX;ty=e.touches[0].clientY;});
  document.addEventListener("touchend",function(e){
    var dx=e.changedTouches[0].clientX-tx,dy=e.changedTouches[0].clientY-ty;
    if(Math.abs(dx)>Math.abs(dy)) move(dx>0?"right":"left");
    else move(dy>0?"down":"up");
  });

  window.addEventListener("message",function(e){
    if(e.data&&e.data.structuredContent) init(e.data.structuredContent.gridSize||4);
  });
})();
<\/script>
</body>
</html>`;
}
