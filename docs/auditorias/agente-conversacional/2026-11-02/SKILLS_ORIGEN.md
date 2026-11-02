# Procedencia de skills

Inspección e instalación local: 02/11/2026. Lectura explícita de las guías; descubrimiento automático disponible en el siguiente turno. No se ejecutaron scripts descargados ni se instalaron plugins Copilot.

| Skill | Fuente oficial / commit fijado | Licencia | Archivos |
|---|---|---|---|
| systematic-debugging | https://github.com/obra/superpowers/tree/2ce8aa15d05bd57213e6f746f953aafb7ded7a8b/skills/systematic-debugging | MIT, Jesse Vincent | Directorio completo (11 archivos) + LICENSE raíz |
| code-testing | https://github.com/dotnet/skills/tree/95e7d0cb7994d47b5789baa68e13f7a2f348b956/plugins/dotnet-test/skills/code-testing | MIT, .NET Foundation | SKILL.md, unit-test-generation.prompt.md + LICENSE raíz |

Ubicación: `.agents/skills/{nombre}/`. Directorios previamente ausentes; ninguna sobrescritura. Licencias conservadas. Frontmatter y referencias locales comprobados por lectura. El script find-polluter.sh y el ejemplo TypeScript se inspeccionaron; no son necesarios para xUnit y no se ejecutaron. Las referencias opcionales a otras skills/agents no constituyen disponibilidad de esas capacidades. No hay catálogo code-testing-extensions: se reutiliza net10.0/xUnit/Moq del proyecto.

El instalador oficial de Codex descargó systematic-debugging a temporal; el de .NET falló por FileNotFoundError en una ruta larga ajena a code-testing. Alternativa: descarga de sus dos archivos oficiales fijados, sin extraer el resto del repositorio. Se revisaron las licencias antes de copiar. No se clonó un ecosistema de agentes.

Inventario copiado systematic-debugging: `SKILL.md`, `CREATION-LOG.md`, `root-cause-tracing.md`, `defense-in-depth.md`, `condition-based-waiting.md`, `condition-based-waiting-example.ts`, `find-polluter.sh`, `test-academic.md`, `test-pressure-1.md`, `test-pressure-2.md`, `test-pressure-3.md`, `LICENSE`. code-testing: `SKILL.md`, `unit-test-generation.prompt.md`, `LICENSE`. La metodología de reproducción/TDD y verificación se aplica; las referencias a otras skills y al agente test-engineer no se instalan ni se presentan como disponibles.
