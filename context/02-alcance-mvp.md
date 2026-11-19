# 02 · Alcance del MVP

## Dentro del MVP

| # | Capacidad | Necesidad | HU | CU | RQ | Prioridad RQ |
|---|---|---|---|---|---|---|
| 1 | Ingreso con correo + contraseña + TOTP, menú por rol, encabezado con nombre/rol/último ingreso | N4 | HU_002 | CU_002 (flujo principal) | RQ_003, RQ_005 | Alta |
| 2 | Crear estrategia asociada a entidad | N1 | HU_003 | CU_003 | RQ_006 | Alta |
| 3 | Editar estrategia solo con 0 vinculados; bloqueo visible y en BD | N1 | HU_003 | CU_004 | RQ_007 | Alta |
| 4 | Buscar estudiante por documento (huella) y vincular, sin duplicados | N1 | HU_004 | CU_005 (flujo principal) | RQ_008 | Alta |
| 5 | Plan mensual individual/grupal con hitos semanales | N2 | HU_005 | CU_006 | RQ_010 | Alta |
| 6 | Registro de seguimiento semanal con asistencia, actividades, avances y fotos | N2 | HU_006 | CU_007 (principal) | RQ_011 | Alta |
| 7 | Mismo registro sin señal, sincronización al volver, resolución de conflictos | N2 | HU_006 | CU_007 (alterno) | RQ_012 | Alta |
| T1 | Bitácora de auditoría inmutable (cadena SHA-256) | N4 | — | transversal | RQ_005 | Alta |
| T2 | Cifrado por columna + huella HMAC de datos del estudiante | N4 | — | transversal | RQ_026 | Alta |
| T3 | Modelo particionado e indexado; seed con volumen | N4 | — | transversal | RQ_002 | Alta |

Todos son de la **línea base / ruta crítica** de la propuesta (Sprints 1–3). Eso es un argumento de venta: "el MVP es exactamente la ruta crítica".

## Fuera del MVP (no construir)

- Habilitación de participantes y entes de control por UI (RQ_001) → los usuarios vienen del realm importado y del seed.
- Federación con Entra ID del MEN (RQ_004, flujo alterno de CU_002).
- Vinculación masiva asíncrona por archivo (RQ_009, flujo alterno de CU_005).
- Todo lo de la Línea B (pilotaje): sesiones, asistencia, latidos, incidentes, alertas, instrumento SUS, informe (RQ_017–RQ_023).
- Reportes asíncronos, WAF, alta disponibilidad, observabilidad Azure, escalado (RQ_015, RQ_021, RQ_024, RQ_025, RQ_028, RQ_029).
- Apps nativas.

Los ítems de menú de esos módulos pueden **aparecer deshabilitados** con la etiqueta "Próximo sprint" para mostrar el producto completo (p. ej. 'Pilotaje' y 'Ayuda' en el menú del Tutor Líder, como dice HU_002).

## Stretch (solo si los 7 puntos del MVP están estables, en este orden)

1. **Confirmación del Tutor Docente** (HU_009 / CU_010 / RQ_016) — reutiliza el mecanismo offline; cierra el ciclo "quien da y quien recibe".
2. **Tablero de desempeño de tutores** (HU_007 / RQ_013) — vista materializada simple + ECharts; muy vendedor visualmente.
3. **Reporte de ley con huella SHA-256 y verificación pública** (HU_008 / RQ_014), un solo reporte en PDF, aislamiento por organización con RLS.
4. Tema visual de Keycloak con la marca Sinapsis Andina / SSES.

## Criterios de aceptación del MVP (lo que la demo debe probar)

- [ ] Un usuario sin TOTP correcto no entra; 5 intentos fallidos bloquean 15 min.
- [ ] El Gestor solo ve el menú 'Estrategias'; el Tutor Líder solo 'Acompañamientos', 'Pilotaje', 'Ayuda'. Un 403 al llamar endpoints de otro rol.
- [ ] Crear estrategia con nombre duplicado en la misma entidad → error claro (409). Fecha fin ≤ inicio → error (400/422).
- [ ] Estrategia nueva queda 'Editable', contador en 0, se puede editar.
- [ ] Buscar un estudiante por tipo+número de documento devuelve nombre, sede y municipio en < 300 ms con el volumen sembrado.
- [ ] Al vincular el primer estudiante la estrategia pasa a 'Bloqueada para edición' y los campos se deshabilitan.
- [ ] Un `UPDATE` directo por `psql` a una estrategia bloqueada falla con el trigger (P0001).
- [ ] Vincular dos veces al mismo estudiante no duplica (respuesta indica `yaVinculados`).
- [ ] El Tutor Líder programa un plan grupal con 2+ docentes y 3 semanas; ve el calendario con cumplimiento 0 %.
- [ ] Registrar una semana en línea la pone en verde 'Registrada' y sube el cumplimiento.
- [ ] Con la red cortada (DevTools Offline o modo avión), la app abre desde caché, muestra el aviso 'Sin conexión…', guarda el registro como 'Pendiente'; al volver la red pasa a 'Sincronizado' sin reescribir nada.
- [ ] Reenviar la misma sincronización no crea duplicados (idempotencia).
- [ ] Si el hito cambió en el servidor mientras se estaba offline, el tutor ve 'En conflicto' con ambas versiones y elige una.
- [ ] Cada una de esas acciones aparece en la bitácora y la verificación de la cadena de hashes da OK.
