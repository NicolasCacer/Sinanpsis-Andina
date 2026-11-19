import { useEffect, useRef } from 'react'
import * as echarts from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
echarts.use([BarChart, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer])

export type PorRegion = { region: string; tutores: number; cumplimiento: number; regularidad: number }

/** Barras por región: cumplimiento y regularidad (%). Apache ECharts 5.6 (RQ_013). */
export default function GraficoRegiones({ datos }: { datos: PorRegion[] }) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!ref.current) return
    const c = echarts.init(ref.current)
    c.setOption({
      animation: false, tooltip: { trigger: 'axis', valueFormatter: (v: number) => `${v} %` }, legend: { top: 0 },
      grid: { left: 40, right: 12, top: 36, bottom: 28 },
      xAxis: { type: 'category', data: datos.map((d) => d.region) },
      yAxis: { type: 'value', max: 100, axisLabel: { formatter: '{value} %' } },
      series: [
        { name: 'Cumplimiento', type: 'bar', data: datos.map((d) => d.cumplimiento), itemStyle: { color: '#14538a' } },
        { name: 'Regularidad', type: 'bar', data: datos.map((d) => d.regularidad), itemStyle: { color: '#1f9d63' } },
      ],
    })
    const ro = new ResizeObserver(() => c.resize()); ro.observe(ref.current)
    return () => { ro.disconnect(); c.dispose() }
  }, [datos])
  return <div ref={ref} role="img" aria-label={`Cumplimiento y regularidad por región: ${datos.map((d) => `${d.region} ${d.cumplimiento} %`).join(', ')}`} className="h-64 w-full" data-testid="grafico-regiones" />
}
