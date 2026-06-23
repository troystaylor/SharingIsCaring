/**
 * Snake Game — classic arcade game.
 * Arrow keys to move, eat food to grow, don't hit walls or yourself.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

export function registerSnake(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/snake.html";

  server.resource("Snake Game UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(snakeHtml()),
    }],
  }));

  server.registerTool(
    "play_snake",
    {
      description: "Play the classic Snake game. Use arrow keys to move, eat food to grow. Don't hit walls or yourself!",
      inputSchema: {
        speed: z.enum(["slow", "normal", "fast"]).optional().describe("Game speed (default: normal)"),
      },
      annotations: { readOnlyHint: true },
      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const speed = args.speed || "normal";
      const interval = speed === "slow" ? 200 : speed === "fast" ? 80 : 120;
      return {
        content: [{ type: "text" as const, text: `Snake game launched (${speed} speed). Use arrow keys to play!` }],
        structuredContent: { speed, interval },
      };
    }
  );
}

function snakeHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Snake</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#1a1a2e; display:flex; flex-direction:column;
    align-items:center; justify-content:center; min-height:100vh; color:#fff; }
  canvas { border:2px solid #4361ee; border-radius:4px; background:#0d0d1a; }
  .hud { display:flex; gap:24px; margin:12px 0; font-size:14px; }
  .hud span { color:#888; } .hud strong { color:#4361ee; }
  .msg { font-size:13px; color:#666; margin-top:8px; }
  .gameover { display:none; font-size:20px; font-weight:700; color:#f72585; margin-top:12px; }
</style>
</head>
<body>
<div class="hud"><span>Score: <strong id="score">0</strong></span><span>High: <strong id="high">0</strong></span></div>
<canvas id="c" width="400" height="400"></canvas>
<div class="msg" id="msg">Arrow keys or swipe to play</div>
<div class="gameover" id="over">Game Over! Tap to restart</div>
<script>
(function(){
  var canvas=document.getElementById("c"),ctx=canvas.getContext("2d");
  var G=20,W=canvas.width/G,H=canvas.height/G;
  var snake,dir,food,score,high=0,alive,interval=120,timer;

  function init(){
    snake=[{x:Math.floor(W/2),y:Math.floor(H/2)}];
    dir={x:1,y:0}; score=0; alive=true;
    document.getElementById("over").style.display="none";
    document.getElementById("msg").style.display="block";
    placeFood(); updateHud();
    if(timer)clearInterval(timer);
    timer=setInterval(tick,interval);
  }

  function placeFood(){
    do{ food={x:Math.floor(Math.random()*W),y:Math.floor(Math.random()*H)}; }
    while(snake.some(function(s){return s.x===food.x&&s.y===food.y;}));
  }

  function tick(){
    if(!alive)return;
    var head={x:snake[0].x+dir.x,y:snake[0].y+dir.y};
    if(head.x<0||head.x>=W||head.y<0||head.y>=H||snake.some(function(s){return s.x===head.x&&s.y===head.y;})){
      alive=false; if(score>high){high=score;} updateHud();
      document.getElementById("over").style.display="block";
      document.getElementById("msg").style.display="none";
      clearInterval(timer); return;
    }
    snake.unshift(head);
    if(head.x===food.x&&head.y===food.y){ score++; updateHud(); placeFood(); }
    else{ snake.pop(); }
    draw();
  }

  function draw(){
    ctx.fillStyle="#0d0d1a"; ctx.fillRect(0,0,canvas.width,canvas.height);
    ctx.fillStyle="#2e7d32";
    snake.forEach(function(s,i){
      ctx.fillStyle=i===0?"#4361ee":"#3451de";
      ctx.fillRect(s.x*G+1,s.y*G+1,G-2,G-2);
    });
    ctx.fillStyle="#f72585";
    ctx.beginPath(); ctx.arc(food.x*G+G/2,food.y*G+G/2,G/2-2,0,Math.PI*2); ctx.fill();
  }

  function updateHud(){
    document.getElementById("score").textContent=score;
    document.getElementById("high").textContent=high;
  }

  document.addEventListener("keydown",function(e){
    if(!alive&&(e.key==="Enter"||e.key===" ")){init();return;}
    var k=e.key;
    if((k==="ArrowUp"||k==="w")&&dir.y!==1) dir={x:0,y:-1};
    if((k==="ArrowDown"||k==="s")&&dir.y!==-1) dir={x:0,y:1};
    if((k==="ArrowLeft"||k==="a")&&dir.x!==1) dir={x:-1,y:0};
    if((k==="ArrowRight"||k==="d")&&dir.x!==-1) dir={x:1,y:0};
    e.preventDefault();
  });

  // Touch controls
  var tx=0,ty=0;
  canvas.addEventListener("touchstart",function(e){tx=e.touches[0].clientX;ty=e.touches[0].clientY;e.preventDefault();});
  canvas.addEventListener("touchend",function(e){
    if(!alive){init();return;}
    var dx=e.changedTouches[0].clientX-tx,dy=e.changedTouches[0].clientY-ty;
    if(Math.abs(dx)>Math.abs(dy)){dir=dx>0?{x:1,y:0}:{x:-1,y:0};}
    else{dir=dy>0?{x:0,y:1}:{x:0,y:-1};}
    e.preventDefault();
  });

  document.getElementById("over").addEventListener("click",init);

  window.addEventListener("message",function(e){
    if(e.data&&e.data.structuredContent){
      interval=e.data.structuredContent.interval||120;
      init();
    }
  });
})();
<\/script>
</body>
</html>`;
}
