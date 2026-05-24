# Planificación de mi TFG (NeuroForge)
```mermaid
%%{init: { 'theme': 'dark', 'themeVariables': { 'fontSize': '24px'}, 'gantt': { 'titlePadding': 0, 'barGap': 4, 'barHeight': 20, 'fontSize': 14 } } }%%gantt
    title Planificación del TFG (NeuroForge) - Parte 1
    dateFormat YYYY-MM-DD
    axisFormat %d %b
    todayMarker off

    section Documentación
    Redacción de la memoria                 :doc, 2026-02-05, 2026-04-05

    section Etapas Iniciales Globales
    Análisis de requisitos y casos de uso   :t_analisis, 2026-02-05, 2026-02-08
    Diseño de la arquitectura y datos       :t_diseno, after t_analisis, 2026-02-12

    section Iteración 1: Juego Base
    Núcleo jugable                          :i1, after t_diseno, 2026-02-26
    Tablero y casillas                      :a1, after t_diseno, 1d
    Definición de piezas                    :a2, after a1, 2d
    Sistema de movimiento                   :a3, after a2, 2d
    Sistema de combate                      :a4, after a3, 2d
    Sistema de ocultación de piezas         :a5, after a4, 1d
    Despliegue aleatorio del bot            :a6, after a5, 1d
    Bot de movimiento aleatorio             :a7, after a6, 1d
    Sistema de despliegue del jugador       :a8, after a7, 2d
    Condición de fin de partida             :a9, after a8, 1d

    section Iteración 2: Interacción
    Interfaz de usuario y UI                :i2, after i1, 2026-04-05
    Bocetado de interfaces                  :b1, after i1, 5d
    Búsqueda e integración de assets        :b2, after b1, 5d
    Animaciones de movimiento de piezas     :b3, after b2, 1d
    Rediseño visual del tablero             :b4, after b3, 3d
    Sprites de las piezas                   :b5, after b4, 6d
    Interfaz de despliegue mejorada         :b6, after b5, 3d
    Interfaz de resolución de combate       :b7, after b6, 4d
    Pantalla de fin de partida              :b8, after b7, 1d
    Menú principal                          :b9, after b8, 1d
    Menú de pausa                           :b10, after b9, 1d
    Menú de reglas                          :b11, after b10, 5d
    Ajustes y correcciones                  :b12, after b11, 2d