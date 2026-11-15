# Videos de diseño aplicados a Beyond Small

Tres videos transcritos completos (Whisper large-v3 en GPU) con fotogramas clave y lo que se cambio en el juego por cada consejo:

| Video | Archivo | Idea central |
|---|---|---|
| Alva Majo · *Como diseñar videojuegos con sentido* | [alva-majo-disenar-con-sentido.md](alva-majo-disenar-con-sentido.md) | Decide que quieres transmitir y añade solo lo que lo consiga |
| Alva Majo · *Mejora la jugabilidad un 500%* | [alva-majo-mejora-la-jugabilidad.md](alva-majo-mejora-la-jugabilidad.md) | Nueve trucos: coyote time, input buffer, camara, feedback... |
| Guinxu · *JUICE: la clave para que un juego se sienta vivo* | [guinxu-juice.md](guinxu-juice.md) | Que cada accion se sienta: temblor, squash & stretch, particulas, textos vivos |

## La vision de Beyond Small despues de los videos

- **Que transmite:** la fantasia de ser diminuto en una casa gigante.
- **Verbos principales:** *explorar* (correr, saltar, trepar, planear) y *pelear*.
- **Objetivo claro:** encender la cabina con 4 baterias verdes que guardan cuatro "colosos" (Reina Hormiga, Reina Abeja, Rey Rata y Bolita).

## Cambios en el juego

**Control (Alva Majo)**, en `Assets/Personaje_principal/PlayerController.cs`:
- Coyote time (0,15 s) y buffer de salto (0,18 s).
- Salto variable: soltar el boton corta el salto, mantenerlo flota un poco en la cumbre.
- Correccion de esquinas al chocar la cabeza.
- Ya no te puedes levantar de agachado con techo encima.
- Buffer del clic de ataque.
- El golpe cargado pega el doble.

**Feedback (Alva Majo)**:
- Numeros de daño.
- Aviso con chispas grises y un "clink" cuando el golpe no hace nada.
- Sin stamina, la barra se agita en rojo y suena un resoplido.
- 0,35 s de invulnerabilidad tras recibir un golpe.

**Camara (Alva Majo y Guinxu)**:
- Inercia y lookahead.
- Mas amortiguacion vertical, para que los saltos no den tirones.
- Se abre un poco al correr.

**Juice (Guinxu)**, en `Assets/Juice/`:
- Temblor de camara, congelar frames, destello rojo y chispas.
- Squash & stretch del jugador y de los bichos golpeados.
- Polvo y sonido al aterrizar.
- Muerte con camara lenta.
- Loot que aparece con rebote.
- Textos que se escriben solos, con pausas y palabras que tiemblan o bailan.
- Todo se regula en *Ajustes > Controles > Temblor y efectos de golpe*.

**Progresion (Alva Majo)**, en `Assets/Progresion/`:
- **Objetivos:** la cabina rastrea las señales de las baterias, la brujula las señala y el HUD muestra el objetivo actual con pistas. Hay avisos de preparacion antes de cada jefe y un recuento de palancas en Hamsterlandia. Se apaga en *Ajustes > Pistas del objetivo*.
- **Mejoras crafteables con el loot que antes no servia:**
  - Guantes (Colmillo o Aguijon + Piel)
  - Botas (Pata de ciempies + Piel)
  - Planeador (Alas + Hueso)
  - Peto (Cuerno + Exoesqueleto)
- **Refugios:** al morir despiertas en la entrada de la zona donde caiste, si ya la conocias, en vez de volver siempre a la cabina.

## Segunda ronda: todo tiene uso, fusiones, misiones y logros

Ver [../Objetos-recetas-logros.md](../Objetos-recetas-logros.md).

- **Todo tiene uso:** cada material del mundo o de los jefes entra en alguna receta (la pistola de Chejov).
- **8 armas fusionadas** con modelos hechos por codigo, en `Assets/Armas`. Cada una cambia como peleas: alcance, area, veneno, rompe-corazas, empuje. Se cambian con Q.
- **Libro de recetas estilo Minecraft** en el inventario. Dice que te falta y donde encontrarlo.
- **Venda** (tela + liga), que se usa con la tecla H.
- **Misiones** (una por jefe mas la final, con recompensas) y **23 logros** con progreso, en un cuaderno que se abre con J.
- Tarjetas de papel al desbloquear algo, y el "siguiente logro" siempre visible en el HUD.
