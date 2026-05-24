# Planificación de mi TFG (NeuroForge)
```mermaid
%%{init: { 'theme': 'dark', 'themeVariables': { 'fontSize': '24px'}, 'gantt': { 'titlePadding': 0, 'barGap': 4, 'barHeight': 20, 'fontSize': 14 } } }%%gantt
    title Planificación del TFG (NeuroForge) - Parte 2
    dateFormat YYYY-MM-DD
    axisFormat %d %b
    todayMarker off

    section Documentación
    Redacción de la memoria                 :doc, 2026-04-05, 2026-06-14

    section Iteración 3: Feedback
    Mejoras audiovisuales                   :i3, 2026-04-05, 2026-04-19
    Fondos y elementos decorativos          :c1, 2026-04-05, 2d
    Efectos de sonido                       :c2, after c1, 5d
    Música                                  :c3, after c2, 3d
    Menú de opciones de audio               :c4, after c3, 3d

    section Iteración 4: Estrategia
    Bot inteligente (IA)                    :i4, after i3, 2026-05-31
    Desarrollo de la lógica del modelo      :d1, after i3, 23d
    Entrenamiento del modelo                :d2, after d1, 10d
    Integración del bot inteligente         :d3, after d2, 3d
    Evaluación del comportamiento           :d4, after d3, 4d

    section Iteración 5: Producción
    Pulido final e integración              :i5, after i4, 2026-06-07
    Optimización del rendimiento            :e1, after i4, 2d
    Revisión del feedback al jugador        :e2, after e1, 2d
    Corrección de errores                   :e3, after e2, 2d
    Pruebas de integración                  :e4, after e3, 1d