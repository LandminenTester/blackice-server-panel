<script lang="ts">
import {onMount} from 'svelte';
let canvas=$state<HTMLCanvasElement>();
onMount(()=>{
  const el=canvas!;const ctx=el.getContext('2d');if(!ctx)return;
  const reduce=matchMedia('(prefers-reduced-motion: reduce)');
  // Same hexagon line lattice as the original static pattern (small 24px cells), revealed only inside soft moving light spots.
  const W=24,R=W/Math.sqrt(3),RED=[255,100,119],CYAN=[116,231,233];
  type Spot={red:boolean;r:number;vx:number;vy:number;x0:number;y0:number;amp:number;freq:number;phase:number};
  const spots:Spot[]=Array.from({length:6},(_,i)=>({red:i%2===0,r:240+((i*97)%170),vx:(i%2?-1:1)*(30+i*5),vy:((i*13)%9-4)*3,x0:i*210,y0:120+i*130,amp:60+i*18,freq:.05+i*.012,phase:i*1.9}));
  let w=0,h=0,raf=0,last=0;const start=performance.now();
  const vertex=(cx:number,cy:number,i:number):[number,number]=>{const a=Math.PI/180*(60*i-30);return [cx+R*Math.cos(a),cy+R*Math.sin(a)];};
  function resize(){const dpr=Math.min(devicePixelRatio||1,1.5);w=innerWidth;h=innerHeight;el.width=Math.round(w*dpr);el.height=Math.round(h*dpr);ctx!.setTransform(dpr,0,0,dpr,0,0);}
  function frame(t:number){
    const c=ctx!;c.clearRect(0,0,w,h);c.lineWidth=1;c.lineJoin='round';
    const span=w+900;
    const pos=spots.map(s=>{const x=((s.x0+t*s.vx)%span+span)%span-450;const y=((s.y0+t*s.vy)%(h+200)+h+200)%(h+200)-100+Math.sin(t*s.freq*6+s.phase)*s.amp;return [x,y,s] as [number,number,Spot];});
    // soft glows under the lattice so the light spots read as light, not just lines
    for(const [sx,sy,s] of pos){const col=s.red?RED:CYAN;const g=c.createRadialGradient(sx,sy,0,sx,sy,s.r);g.addColorStop(0,`rgba(${col[0]},${col[1]},${col[2]},.035)`);g.addColorStop(1,`rgba(${col[0]},${col[1]},${col[2]},0)`);c.fillStyle=g;c.fillRect(sx-s.r,sy-s.r,s.r*2,s.r*2);}
    // slow diagonal wave bands sweeping over the screen
    const bandA=.52,bx=Math.cos(bandA),by=Math.sin(bandA);
    const cols=Math.ceil(w/W)+2,rows=Math.ceil(h/(R*1.5))+2;
    c.strokeStyle='rgba(255,100,119,.0025)';c.beginPath();const quiet=c;
    const lit:{x:number;y:number;style:string}[]=[];
    for(let r=-1;r<rows;r++)for(let q=-1;q<cols;q++){
      const cx=q*W+(r&1?W/2:0),cy=r*R*1.5;
      let ir=0,ic=0;
      for(const [sx,sy,s] of pos){const dx=cx-sx,dy=cy-sy,g=Math.exp(-(dx*dx+dy*dy)/(s.r*s.r));if(s.red)ir+=g;else ic+=g;}
      const k=(cx*bx+cy*by)/260-t*.18,band=Math.pow(Math.max(0,Math.sin(k)),10)*.55;
      const gx=Math.min(1,Math.max(0,(cx/w+cy/h)/2));
      ir+=band*(1-gx);ic+=band*gx;
      const sum=ir+ic,inten=Math.min(1,sum);
      const alpha=.0025+.052*Math.pow(inten,.8);
      const mr=sum>0?ir/sum:1,mc=1-mr;
      const red=Math.round(RED[0]*mr+CYAN[0]*mc),green=Math.round(RED[1]*mr+CYAN[1]*mc),blue=Math.round(RED[2]*mr+CYAN[2]*mc);
      if(inten<.03){for(let i=0;i<3;i++){const [x1,y1]=vertex(cx,cy,i),[x2,y2]=vertex(cx,cy,i+1);quiet.moveTo(x1,y1);quiet.lineTo(x2,y2);}continue;}
      lit.push({x:cx,y:cy,style:`rgba(${red},${green},${blue},${alpha.toFixed(3)})`});
    }
    c.stroke();
    for(const cell of lit){
      c.strokeStyle=cell.style;c.beginPath();
      for(let i=0;i<3;i++){const [x1,y1]=vertex(cell.x,cell.y,i),[x2,y2]=vertex(cell.x,cell.y,i+1);c.moveTo(x1,y1);c.lineTo(x2,y2);}
      c.stroke();
    }
  }
  function loop(now:number){raf=requestAnimationFrame(loop);if(now-last<33)return;last=now;frame((now-start)/1000);}
  function begin(){cancelAnimationFrame(raf);if(reduce.matches){frame(20);}else raf=requestAnimationFrame(loop);}
  resize();begin();
  const onResize=()=>{resize();if(reduce.matches)frame(20);};
  addEventListener('resize',onResize);reduce.addEventListener('change',begin);
  return()=>{cancelAnimationFrame(raf);removeEventListener('resize',onResize);reduce.removeEventListener('change',begin);};
});
</script>
<canvas bind:this={canvas} class="hex-bg" aria-hidden="true"></canvas>
<style>
.hex-bg{position:fixed;inset:0;width:100%;height:100%;z-index:-1;pointer-events:none}
</style>
