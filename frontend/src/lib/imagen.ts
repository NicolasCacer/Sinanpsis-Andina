// RQ_011: las fotos se reducen en el navegador a 1.280 px (lado mayor), calidad 0,7, JPEG/WebP ≤ 1 MB.
export const MAX_LADO = 1280
export const CALIDAD = 0.7
export const MAX_BYTES = 1_048_576

export function calcularDimensiones(ancho: number, alto: number, max = MAX_LADO): { ancho: number; alto: number } {
  const mayor = Math.max(ancho, alto)
  if (mayor <= max) return { ancho, alto }
  const f = max / mayor
  return { ancho: Math.max(1, Math.round(ancho * f)), alto: Math.max(1, Math.round(alto * f)) }
}

async function aBlob(canvas: HTMLCanvasElement, tipo: string, calidad: number): Promise<Blob | null> {
  return new Promise((res) => canvas.toBlob((b) => res(b), tipo, calidad))
}

/** Devuelve un Blob WebP (o JPEG si el navegador no codifica WebP) de máximo 1 MB. */
export async function reducirImagen(archivo: Blob): Promise<Blob> {
  const bmp = await createImageBitmap(archivo)
  const { ancho, alto } = calcularDimensiones(bmp.width, bmp.height)
  const canvas = document.createElement('canvas'); canvas.width = ancho; canvas.height = alto
  canvas.getContext('2d')!.drawImage(bmp, 0, 0, ancho, alto); bmp.close()
  let tipo = 'image/webp'
  let b = await aBlob(canvas, tipo, CALIDAD)
  if (!b || b.type !== 'image/webp') { tipo = 'image/jpeg'; b = await aBlob(canvas, tipo, CALIDAD) }
  for (let q = CALIDAD - 0.1; b && b.size > MAX_BYTES && q > 0.1; q -= 0.1) b = await aBlob(canvas, tipo, q)
  if (!b || b.size > MAX_BYTES) throw new Error('No se pudo reducir la imagen a 1 MB.')
  return b
}
