import { describe, expect, it } from 'vitest'
import { calcularDimensiones } from './imagen'

describe('calcularDimensiones', () => {
  it('reduce el lado mayor a 1280 conservando la proporción', () => {
    expect(calcularDimensiones(4000, 3000)).toEqual({ ancho: 1280, alto: 960 })
    expect(calcularDimensiones(3000, 4000)).toEqual({ ancho: 960, alto: 1280 })
  })
  it('no agranda imágenes pequeñas', () => { expect(calcularDimensiones(800, 600)).toEqual({ ancho: 800, alto: 600 }) })
  it('nunca devuelve dimensiones menores a 1', () => { expect(calcularDimensiones(100000, 10).alto).toBeGreaterThanOrEqual(1) })
})
