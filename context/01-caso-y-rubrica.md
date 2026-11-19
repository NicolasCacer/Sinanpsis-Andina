# 01 · Caso y rúbrica

## El caso (pliego)

- **Cliente:** Ministerio de Educación Nacional (MEN), Oficina de Tecnología.
- **Objeto:** diseñar, desarrollar, implementar y operar un **piloto** del SSES (Sistema de Seguimiento de Actividades Extracurriculares), con infraestructura, soporte y ejercicio controlado con usuarios a nivel nacional.
- **Problema actual:** todo vive en hojas de cálculo compartidas → sin trazabilidad, integridad, control ni disponibilidad.
- **Alcance funcional del pliego:**
  - Crear estrategias estudiantiles asociadas a entidades ya definidas.
  - Asociar estudiantes **precargados**; la estrategia solo es editable **mientras no tenga estudiantes vinculados**.
  - Acompañamientos pedagógicos a docentes (precargados y asociados a estrategias), individual y grupal, **periodicidad mensual con seguimiento semanal**.
  - Reportes y análisis (desempeño de tutores), acceso de entes de control a reportes, reportes de ley.
  - Millones de registros, usuarios concurrentes, condiciones reales nacionales.
- **Valoración especial:** conectividad limitada y alta concurrencia; capacidad de gestionar el pilotaje con usuarios.
- **Plazo:** 5 meses. **Presupuesto:** $200.000.000 COP.
- **Entregables del pliego:** (1) análisis y diseño, (2) estrategia de gerencia **con un MVP**, (3) infraestructura, (4) registros de sesiones y reportes, (5) percepción de tutores, (6) informe integral.

Texto completo: `fuentes/pliego-sses.pdf`.

## La rúbrica (parcial tercer corte)

| Componente | Puntos | Dónde se resuelve |
|---|---|---|
| Preguntas de concepto (5) | 5 | Documento PDF (ya entregado) |
| Necesidades e Historias de Usuario | 15 | Documento PDF |
| Casos de Uso, Requerimientos y Plan de QA | 15 | Documento PDF |
| Diagramas de secuencia, clases y despliegue | 15 | Documento PDF |
| Gerencia de Proyectos | 20 | Documento PDF |
| **Presentación presencial: comercial, vendiendo la propuesta con MVP (desarrollo de software)** | **30** | **Este repo** |

Nota del grupo = total / 2. El documento ya se entregó (viernes 13 nov 2026, 9:50 a. m.).

## Qué necesita la presentación de este MVP

La presentación es **comercial**: Sinapsis Andina le vende al Ministerio. El MVP tiene que hacer creíble la propuesta, así que debe:

1. **Funcionar en vivo** sin fallos, con un guion de 5–7 minutos (ver `08-semilla-y-demo.md`).
2. **Responder a lo que el pliego valora**: conectividad limitada (modo sin conexión real), volumen (búsqueda rápida sobre cientos de miles/millones de filas), seguridad de datos de menores (2FA, cifrado, bitácora).
3. **Mostrar la regla central del pliego** (bloqueo de estrategias con estudiantes) de forma visible e irrompible.
4. **Ser coherente con el documento entregado**: mismos actores, nombres de pantallas, roles, IDs de HU/CU/RQ y stack. Si el jurado pregunta "¿esto es lo que pusieron en el documento?", la respuesta es sí.
5. **Verse profesional** en portátil y en celular (el Tutor Líder trabaja desde el celular en campo).

## Datos administrativos

- Empresa: **Sinapsis Andina S.A.S.** — "Tecnología que llega a cada rincón del país".
- Integrantes: Nicolas Joel Cáceres Parra, Carmen Celeste Durán Báez, Elioth Thomás Gómez Morales, Camilo Andrés Otálora Riveros, Mauricio Suárez Barrera.
- Asignatura: Gestión de Proyectos de Software — Docente: Didier Mauricio Calderón Novoa. Universidad de La Sabana, Chía.
- **Fecha de la presentación:** _TODO — completar_.
- **Equipo para la demo (portátil, celular, red):** _TODO — completar_.
