# 08 · Datos semilla y guion de la demo

## Datos sintéticos (`tools/Sses.Seed`)

Determinista (semilla fija) para que la demo sea reproducible. Parámetro `--estudiantes N` (por defecto **500.000**; probar 1.000.000 si el portátil aguanta; diseño de la propuesta: 10.000.000).

| Dato | Cantidad | Detalle |
|---|---|---|
| Regiones | 6 | Caribe, Pacífico, Andina, Orinoquía, Amazonía, Bogotá D. C. |
| Municipios | ~30 | Reales por región (nombres públicos) |
| Entidades | 40 | Nombres ficticios tipo "Fundación Semillas del Pacífico", "Corporación Raíces Andinas" (1 inactiva para probar 422) |
| Sedes | 300 | "I.E. …" ficticias |
| Estudiantes | N | Nombres generados (Bogus/Faker es-CO), documentos TI/RC/NUIP sintéticos, cifrados + huella. Inserción con `COPY` binario por lotes |
| Tutores líderes | 200 | |
| Tutores docentes | 3.000 | |
| Estrategias precargadas | 60 | Algunas ya `BLOQUEADA` con vinculados, otras `EDITABLE` |
| Asignaciones TL–TD–estrategia | ~3.000 | El TL demo tiene 6 docentes en 2 estrategias |

### Personajes de la demo (fijos)

| Usuario Keycloak | Rol | Persona | Para qué |
|---|---|---|---|
| `gestor.demo@sinapsis.co` | GESTOR_ESTRATEGIAS | Laura Gestora — administra 3 entidades del Pacífico | Estrategias y vinculación |
| `tutor.lider@sinapsis.co` | TUTOR_LIDER | Andrés Tutor Líder — Tumaco, Nariño | Plan mensual y seguimiento offline |
| `tutor.docente@sinapsis.co` | TUTOR_DOCENTE | Marina Docente — asignada al TL demo | Stretch |
| `admin@sinapsis.co` | ADMIN_FUNCIONAL | — | Soporte |

- Contraseña común de demo y **secreto TOTP conocido** (base32) documentado en `.env.example` como `DEMO_TOTP_SECRET`, para cargar en el autenticador del celular de quien presenta. Alternativa: mostrar el código con `oathtool --totp -b $DEMO_TOTP_SECRET` en otra terminal.
- Estudiantes "conocidos" para buscar en vivo (documentos fijos, fáciles de dictar): `TI 1001234567`, `TI 1001234568`, `RC 1102345678`. Uno ya vinculado a otra estrategia (no importa, se puede vincular a varias) y uno inexistente `TI 9999999999` para mostrar 404.

### `scripts/reset-demo.sh`

Restaura el estado inicial del guion: borra estrategias creadas en la demo, planes del mes actual del TL demo, seguimientos e idempotencias de la sesión, sin re-sembrar los N estudiantes (truncar solo tablas transaccionales del TL/gestor demo). La bitácora **no** se borra (es inmutable): se usa una partición/fecha nueva o se acepta que crezca.

## Guion de la demo en vivo (≈ 6 min)

Hilo narrativo: *"Andrés es tutor líder en Tumaco. Hoy el Ministerio no sabe si él hizo su acompañamiento: está en una hoja de cálculo que nadie controla."*

| # | Tiempo | Pantalla / acción | Mensaje comercial | Respalda |
|---|---|---|---|---|
| 1 | 0:30 | Laura entra: correo, contraseña, **código TOTP del celular**. Encabezado con su último ingreso. | Datos de menores protegidos desde la puerta; cada acción queda a su nombre. | N4, RQ_003 |
| 2 | 1:00 | 'Estrategias' → 'Nueva estrategia' en una entidad del Pacífico. Intentar nombre duplicado → error. Guardar → 'Editable', 0 vinculados. Editar un campo → OK. | Reglas del programa aplicadas por el sistema, no por la buena voluntad del funcionario. | N1, RQ_006 |
| 3 | 1:00 | 'Asociar estudiantes' → buscar `TI 1001234567` entre **500.000** registros: resultado al instante (mostrar el tiempo). Vincular 2. La ficha pasa a **'Bloqueada para edición'**. | Escala nacional y la regla central del pliego, visible. | N1, N4, RQ_002, RQ_007, RQ_008 |
| 4 | 0:30 | (Opcional, técnico) En terminal: `UPDATE estrategias.estrategia SET nombre='x' ...` → error del trigger. | "Ni siquiera alguien con acceso a la base puede alterar una estrategia en ejecución." | RQ_007 |
| 5 | 1:00 | Andrés (celular o vista móvil) entra → 'Acompañamientos' → 'Plan mensual' → grupal, 3 docentes, semanas 1-3 → 'Programar'. Calendario con 0 %. | El Ministerio sabe qué acompañamientos se comprometieron. | N2, RQ_010 |
| 6 | 1:30 | **Modo avión / Offline.** Recargar: la app abre. Aviso 'Sin conexión…'. Registrar la semana 1 con asistencia y una foto → 'Pendiente'. **Volver la red** → 'Sincronizado', cumplimiento 33 %. | "En una vereda sin señal el trabajo no se pierde ni se duplica." El pliego lo valora especialmente. | N2, RQ_011, RQ_012 |
| 7 | 0:30 | (Si hay tiempo) Conflicto: otra ventana registra la misma semana online; la offline sincroniza → 'En conflicto', elegir. | Integridad aun con dos dispositivos. | RQ_012 |
| 8 | 0:30 | Bitácora: últimos eventos y "Cadena íntegra ✓". | Trazabilidad que las hojas de cálculo no dan. | N4, RQ_005 |

Cierre: "Esto es el Sprint 3 de nuestro cronograma — semana 9 de 22 — y es exactamente la ruta crítica de la línea base. Lo que falta (reportes de ley, pilotaje, analítica) se construye sobre esta base."

### Plan B (si algo falla en vivo)

- Video grabado del recorrido completo (grabarlo con la versión final; 1080p, sin audio, con subtítulos de cada paso).
- Si falla Keycloak/TOTP: usuarios ya autenticados en pestañas abiertas antes de empezar.
- Si falla la red del salón: todo corre local en el portátil (`docker compose`), y el modo offline se demuestra con DevTools.

### Logística de la demo

- Portátil con Docker; build de producción del front servido por nginx (no `vite dev`).
- Para usar el celular: URL HTTPS (Cloudflare Tunnel o despliegue) → actualizar redirect URIs del cliente `sses-web` y `KC_HOSTNAME`. Instalar la PWA en el celular antes ("Agregar a pantalla de inicio").
- Ensayo completo con `reset-demo.sh` entre corridas; medir que quepa en el tiempo asignado.
