/**
 * Tetris — classic falling blocks game.
 * Arrow keys: left/right to move, up to rotate, down to soft drop, space to hard drop.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

export function registerTetris(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/tetris.html";

  server.resource("Tetris Game UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(tetrisHtml()),
    }],
  }));

  server.registerTool(
    "play_tetris",
    {
      description: "Play Tetris! Arrow keys to move/rotate, space to hard drop. Clear lines to score!",
      inputSchema: {
        level: z.number().min(1).max(20).optional().describe("Starting level 1-20 (default 1). Higher = faster."),
      },
      annotations: { readOnlyHint: true },
      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const level = args.level || 1;
      return {
        content: [{ type: "text" as const, text: `Tetris launched (level ${level}). Arrow keys to play, space to drop!` }],
        structuredContent: { level },
      };
    }
  );
}

function tetrisHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Tetris</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#1a1a2e; display:flex;
    align-items:flex-start; justify-content:center; padding:12px; gap:16px; color:#fff; }
  canvas { border:2px solid #4361ee; background:#0d0d1a; }
  .side { display:flex; flex-direction:column; gap:12px; }
  .box { background:#16213e; border-radius:8px; padding:12px; text-align:center; }
  .box-label { font-size:10px; color:#888; text-transform:uppercase; letter-spacing:1px; }
  .box-value { font-size:24px; font-weight:700; color:#4361ee; margin-top:4px; }
  .next-canvas { background:#0d0d1a; border-radius:4px; margin-top:4px; }
  .controls { font-size:10px; color:#555; line-height:1.6; }
</style>
</head>
<body>
<canvas id="board" width="240" height="480"></canvas>
<div class="side">
  <div class="box"><div class="box-label">Score</div><div class="box-value" id="score">0</div></div>
  <div class="box"><div class="box-label">Lines</div><div class="box-value" id="lines">0</div></div>
  <div class="box"><div class="box-label">Level</div><div class="box-value" id="level">1</div></div>
  <div class="box"><div class="box-label">Next</div><canvas class="next-canvas" id="next" width="80" height="80"></canvas></div>
  <div class="controls">
    Left/Right: Move<br>Up: Rotate<br>Down: Soft drop<br>Space: Hard drop
  </div>
</div>
<script>
(function(){
  var COLS=10,ROWS=20,SZ=24;
  var PIECES=[
    [[1,1,1,1]],
    [[1,1],[1,1]],
    [[0,1,0],[1,1,1]],
    [[1,0,0],[1,1,1]],
    [[0,0,1],[1,1,1]],
    [[1,1,0],[0,1,1]],
    [[0,1,1],[1,1,0]]
  ];
  var COLORS=["#00f0f0","#f0f000","#a000f0","#0000f0","#f0a000","#00f000","#f00000"];
  var board,score,lines,level,cur,curX,curY,curColor,nextPiece,nextColor,timer,alive;
  var canvas=document.getElementById("board"),ctx=canvas.getContext("2d");
  var nextCanvas=document.getElementById("next"),nctx=nextCanvas.getContext("2d");

  function init(startLevel){
    board=[];for(var r=0;r<ROWS;r++){var row=[];for(var c=0;c<COLS;c++)row.push(0);board.push(row);}
    score=0;lines=0;level=startLevel||1;alive=true;
    pickNext(); spawn();
    if(timer)clearInterval(timer);
    timer=setInterval(tick,Math.max(50,500-level*40));
    render();
  }

  function pickNext(){
    var i=Math.floor(Math.random()*PIECES.length);
    nextPiece=PIECES[i]; nextColor=COLORS[i];
  }

  function spawn(){
    cur=nextPiece; curColor=nextColor; pickNext();
    curX=Math.floor((COLS-cur[0].length)/2); curY=0;
    if(collides(cur,curX,curY)){alive=false;clearInterval(timer);}
    drawNext();
  }

  function collides(piece,px,py){
    for(var r=0;r<piece.length;r++) for(var c=0;c<piece[r].length;c++){
      if(!piece[r][c]) continue;
      var nr=py+r,nc=px+c;
      if(nc<0||nc>=COLS||nr>=ROWS) return true;
      if(nr>=0&&board[nr][nc]) return true;
    }
    return false;
  }

  function merge(){
    for(var r=0;r<cur.length;r++) for(var c=0;c<cur[r].length;c++){
      if(cur[r][c]&&curY+r>=0) board[curY+r][curX+c]=curColor;
    }
    clearLines();
    spawn();
  }

  function clearLines(){
    var cleared=0;
    for(var r=ROWS-1;r>=0;r--){
      if(board[r].every(function(c){return c!==0;})){
        board.splice(r,1);
        var empty=[];for(var c2=0;c2<COLS;c2++)empty.push(0);
        board.unshift(empty);
        cleared++; r++;
      }
    }
    if(cleared){
      lines+=cleared;
      score+=[0,100,300,500,800][cleared]*level;
      level=Math.floor(lines/10)+1;
      clearInterval(timer);
      timer=setInterval(tick,Math.max(50,500-level*40));
    }
  }

  function rotate(){
    var rotated=[];
    for(var c=0;c<cur[0].length;c++){
      var row=[];for(var r=cur.length-1;r>=0;r--) row.push(cur[r][c]);
      rotated.push(row);
    }
    if(!collides(rotated,curX,curY)) cur=rotated;
  }

  function tick(){
    if(!alive) return;
    if(!collides(cur,curX,curY+1)) curY++;
    else merge();
    render();
  }

  function render(){
    ctx.fillStyle="#0d0d1a";ctx.fillRect(0,0,canvas.width,canvas.height);
    // Board
    for(var r=0;r<ROWS;r++) for(var c=0;c<COLS;c++){
      if(board[r][c]){ctx.fillStyle=board[r][c];ctx.fillRect(c*SZ+1,r*SZ+1,SZ-2,SZ-2);}
    }
    // Current piece
    if(alive){
      ctx.fillStyle=curColor;
      for(var r2=0;r2<cur.length;r2++) for(var c2=0;c2<cur[r2].length;c2++){
        if(cur[r2][c2]) ctx.fillRect((curX+c2)*SZ+1,(curY+r2)*SZ+1,SZ-2,SZ-2);
      }
    }
    // HUD
    document.getElementById("score").textContent=score;
    document.getElementById("lines").textContent=lines;
    document.getElementById("level").textContent=level;
    if(!alive){
      ctx.fillStyle="rgba(0,0,0,0.6)";ctx.fillRect(0,0,canvas.width,canvas.height);
      ctx.fillStyle="#f72585";ctx.font="bold 24px system-ui";ctx.textAlign="center";
      ctx.fillText("GAME OVER",canvas.width/2,canvas.height/2);
      ctx.fillStyle="#888";ctx.font="14px system-ui";
      ctx.fillText("Press any key to restart",canvas.width/2,canvas.height/2+30);
    }
  }

  function drawNext(){
    nctx.fillStyle="#0d0d1a";nctx.fillRect(0,0,80,80);
    nctx.fillStyle=nextColor;
    var ox=Math.floor((80-nextPiece[0].length*18)/2),oy=Math.floor((80-nextPiece.length*18)/2);
    for(var r=0;r<nextPiece.length;r++) for(var c=0;c<nextPiece[r].length;c++){
      if(nextPiece[r][c]) nctx.fillRect(ox+c*18+1,oy+r*18+1,16,16);
    }
  }

  document.addEventListener("keydown",function(e){
    if(!alive){if(e.key){init(level);} return;}
    if(e.key==="ArrowLeft"&&!collides(cur,curX-1,curY)) curX--;
    else if(e.key==="ArrowRight"&&!collides(cur,curX+1,curY)) curX++;
    else if(e.key==="ArrowDown"&&!collides(cur,curX,curY+1)) curY++;
    else if(e.key==="ArrowUp") rotate();
    else if(e.key===" "){while(!collides(cur,curX,curY+1))curY++;merge();}
    if(["ArrowLeft","ArrowRight","ArrowUp","ArrowDown"," "].indexOf(e.key)!==-1) e.preventDefault();
    render();
  });

  window.addEventListener("message",function(e){
    if(e.data&&e.data.structuredContent) init(e.data.structuredContent.level||1);
  });
})();
<\/script>
</body>
</html>`;
}
