# GUIA_GIT.md — Cómo subir código a QueryDoctor AI

> Flujo para llevar una tarea del plan de sprints (S?-??) desde tu máquina hasta `main`.
> Complementa a `WORKFLOW.md` (qué se verifica) con el **cómo** en Git y GitHub.
> Comandos para PowerShell en Windows. **Ejecútalos de a uno y lee la salida antes del siguiente.**

## Reglas de oro

1. **Nunca se trabaja en `main`.** Siempre en una rama de tarea. GitHub rechaza el push directo a `main`.
2. **Una tarea del plan = una rama = un PR.**
3. **`git status` antes de cada commit.** Si aparece algo que no reconoces (`bin/`, `obj/`, `.env`), no lo agregues.
4. **Nada de `git commit -am`.** Agrega los archivos con `git add` y revisa.
5. **`dotnet format` antes de cada `git add`.** La CI rechaza el formato incorrecto.
6. **Antes de `git reset --hard`, revisa en qué rama estás** con `git branch --show-current`. Borra sin preguntar.
7. **`git push --force` solo sobre tu propia rama, nunca sobre `main`** (de todas formas está bloqueado).
8. **El repo fuera de OneDrive** u otra carpeta sincronizada.

---

## 1. Empezar una tarea

```powershell
git switch main
git pull                                   # trae lo último que se fusionó
git switch -c feat/S1-09-conexiones        # rama nueva desde main actualizado
```

**Nombre de la rama:** `tipo/S?-??-descripcion-corta`

| Tipo | Cuándo |
|---|---|
| `feat/` | Funcionalidad nueva (la mayoría de tareas) |
| `fix/` | Corrección de un error |
| `docs/` | Solo documentación |
| `chore/` | Configuración, dependencias, build |

> Si acabas de fusionar un PR, **no sigas trabajando en esa rama vieja**. Con *squash* su historial ya no
> coincide con `main`. Crea siempre una rama nueva desde `main` actualizado.

---

## 2. Mientras trabajas

### Ciclo de un commit
```powershell
dotnet format                              # corrige formato
dotnet build                               # 0 errores
dotnet test                                # todo en verde
git status                                 # ¿qué cambió? ¿algo raro?
git add src/QueryDoctor.Application/Connections/RegisterConnection.cs
git add tests/QueryDoctor.UnitTests/Connections/RegisterConnectionTests.cs
git status                                 # ¿quedó en verde (staged) solo lo que querías?
git commit -m "feat(S1-09): registrar conexión con validación de versión mínima"
git push                                   # la primera vez: git push -u origin <rama>
```

`git add .` está permitido **solo si** el `git status` anterior muestra exactamente lo que quieres subir.

### Mensajes de commit (Conventional Commits)
```
tipo(S?-??): qué hiciste, en presente y corto
```
`feat` · `fix` · `test` · `docs` · `refactor` · `chore` · `ci` · `style`

Ejemplos: `feat(S1-11): ingesta incremental con marca de agua` ·
`test(S1-14): plan positivo y negativo para REG-04` · `fix(S1-11): evitar filas duplicadas al reintentar`

### Cada cuánto
- **Commit:** cada vez que algo pequeño funciona y las pruebas pasan. Mejor 6 commits claros que 1 gigante.
- **Push:** al menos al final de cada sesión de trabajo. Lo que no está en GitHub no existe si tu PC falla.

---

## 3. Mantener tu rama al día con `main`

Si alguien fusionó algo a `main` mientras trabajabas (sobre todo si toca tus archivos):

```powershell
git status                                 # debe estar limpio; si no, haz commit primero
git fetch origin
git merge origin/main                      # trae main a tu rama
dotnet build
dotnet test
git push
```

**Si hay conflictos:** Git marca los archivos como `both modified`. Ábrelos en VS Code, elige en cada bloque
*Accept Current / Incoming / Both*, guarda, y luego:
```powershell
git add <archivo-resuelto>
git commit                                 # Git ya trae el mensaje del merge
dotnet build
dotnet test
git push
```
Si no entiendes un conflicto en el código de otra persona, **pregúntale antes de resolverlo**.

Usamos `merge` y no `rebase` a propósito: `rebase` obliga a `push --force` y es fácil equivocarse.
Al final el PR se aplasta (*squash*), así que el merge no ensucia `main`.

---

## 4. Antes de abrir el PR

Checklist (es la compuerta VERIFY de `WORKFLOW.md`):

- [ ] `dotnet format --verify-no-changes` sin salida
- [ ] `dotnet build` con 0 errores
- [ ] `dotnet test` en verde, con las pruebas nuevas de tu tarea
- [ ] Cada criterio "Hecho cuando" de la tarea tiene una prueba o pasos manuales que lo demuestran
- [ ] Respetas los invariantes de `CLAUDE.md`
- [ ] `/verify` (y `/audit` en una sesión distinta) si usaste Claude Code
- [ ] `docs/HANDOFF.md` actualizado si tu tarea cambia el estado del proyecto
- [ ] Rama al día con `main` (sección 3)

---

## 5. Abrir el PR

```powershell
gh pr create --base main --title "S1-09: registro de conexiones y CapabilityProbe"
```
- Elige la plantilla `pull_request_template.md` y llénala: tarea, requisitos, qué y **por qué**, evidencia por criterio.
- **Pide la revisión en el mismo momento**, no al final:
  ```powershell
  gh pr edit --add-reviewer <usuario>
  ```
  y avísale por chat.

Si el PR no está listo pero quieres que la CI corra o mostrarlo: `gh pr create --draft`.
Cuando esté listo: `gh pr ready`.

---

## 6. Si la CI falla

```powershell
gh pr checks --watch                       # espera ~15 s después del push antes de usarlo
gh run view --log-failed                   # muestra solo el paso que falló y su error
```

| Paso que falla | Causa típica | Arreglo |
|---|---|---|
| Setup de .NET | Falta `global.json` en el repo | Agregarlo |
| Restaurar paquetes | `NU1902`/`NU1903`: paquete vulnerable | Fijar versión parcheada; nunca suprimir |
| Compilar | Warning convertido en error (CAxxxx, IDExxxx) | Corregir el código |
| Verificar formato | `FINALNEWLINE`, `WHITESPACE` | `dotnet format`, commit, push |
| Pruebas | Prueba roja o regla de arquitectura violada | Corregir el código, **nunca** la prueba para que pase |

Arreglas en tu rama, haces commit y push, y la CI vuelve a correr sola.

---

## 7. Revisión

- **Como autor:** responde cada comentario. Si haces el cambio, haz push y marca **Resolve conversation**.
  Si no estás de acuerdo, explica por qué en el hilo. El PR no se fusiona con conversaciones abiertas.
- **Si haces push después de una aprobación, la aprobación se cae** y hay que pedirla de nuevo
  (protección contra cambios no revisados).
- **Como revisor:** `gh pr checkout <número>` para probarlo en tu máquina.
  `gh pr review <número> --approve` o `--request-changes -b "motivo"`.
  Revisa sobre todo: invariantes, que las pruebas demuestren los criterios, y que **entiendas** el código.

---

## 8. Fusionar y limpiar

```powershell
gh pr checks                               # CI en ✓
gh pr merge --squash                       # el único método permitido
git switch main
git pull
git branch -d feat/S1-09-conexiones        # borra la rama local (GitHub ya borró la remota)
```

Si `git branch -d` se queja de que "no está fusionada": es normal con *squash*. Verifica en GitHub que el PR
dice **Merged** y entonces usa `git branch -D <rama>`.

Después: marcar la tarea como **Hecha** en el plan de sprints.

---

## 9. Cierre de sprint

Cuando todas las tareas del sprint estén fusionadas:

1. **Plan de sprints:** estado de cada tarea actualizado (Hecha / pasa al siguiente sprint con motivo).
2. **Regenerar `docs/sprints/SPRINTS.md`** desde la hoja, en una rama `docs/cierre-sprint-1`, por PR.
3. **`docs/HANDOFF.md`:** estado al cierre y qué arranca el siguiente sprint.
4. **Etiqueta en `main`**, para que el evaluador vea exactamente qué se entregó en cada sprint:
   ```powershell
   git switch main
   git pull
   git tag -a sprint-1 -m "Cierre Sprint 1: <resumen de una línea>"
   git push origin sprint-1
   ```
   Para ver lo que había al cierre de un sprint: `git switch --detach sprint-1`.

---

## 10. Problemas frecuentes

**"Your local changes would be overwritten by checkout"** al cambiar de rama
→ Tienes cambios sin commit. Haz commit en tu rama, o guárdalos temporalmente:
`git stash` → cambias de rama → vuelves → `git stash pop`.

**Hice commits en `main` por error** (el push fue rechazado)
```powershell
git branch --show-current                  # confirma: main
git branch feat/S?-??-rescate              # guarda tus commits en una rama nueva
git reset --hard origin/main               # main local vuelve a como está en GitHub
git switch feat/S?-??-rescate              # sigue aquí y abre el PR
```

**Aparecen `bin/` u `obj/` en `git status`** → falta el `.gitignore` o se dañó. No los agregues.

**Perdí commits con un `reset`** → si alguna vez hiciste commit, Git los guarda unos días:
```powershell
git reflog                                 # busca el commit (ej. a1b2c3d)
git branch rescate a1b2c3d
```
Los cambios **sin commit** no se recuperan con Git; prueba el **Timeline** de VS Code (Local History).

**"Your branch is behind"** → `git pull`.
**"Your branch and 'origin/…' have diverged"** → no hagas `--force`; pregunta antes de tocar nada.

---

## Resumen en una línea por paso

```
main actualizado → rama nueva → commits pequeños (format, build, test, status, add, commit, push)
→ al día con main → checklist → PR + pedir revisión → CI verde → revisión → squash → limpiar → tarea Hecha
```
