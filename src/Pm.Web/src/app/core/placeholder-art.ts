// 漸層 placeholder art(無真圖時的暫代縮圖)。
// presentation 用的共用工具(非假資料),故住 core。
export function artGradient(seed: number): string {
  const h1 = (seed * 137.508) % 360;
  const h2 = (h1 + 38 + ((seed * 53) % 90)) % 360;
  const s = 48 + ((seed * 7) % 24);
  return `linear-gradient(${(seed * 61) % 360}deg, hsl(${h1} ${s}% 47%), hsl(${h2} ${s - 10}% 29%))`;
}
