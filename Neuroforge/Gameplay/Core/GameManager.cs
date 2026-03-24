using Godot;
using System;
using System.Collections.Generic;

public partial class GameManager : Node
{
    // Externo
    private const string BOARD_SCENE_PATH = "res://Gameplay/Board/Board.tscn";

    public static GameManager Instance { get; private set; }    // Instancia que se crea al iniciarse la escena de la partida
    private Board _board;
    private Camera2D _camera;
    private DeploymentUI _deploymentUI;                         // Interfaz del despliegue de piezas
    private DeploymentController _deployment;                   // Logica del despliegue de piezas
    private Random _rng = new Random();

    // Controla el estado y el turno actual de la partida
    public PieceOwner CurrentTurn { get; private set; } = PieceOwner.PLAYER;
    public GameState State { get; private set; } = GameState.WAITING_INPUT;
    public int TurnNumber { get; private set; } = 0;

    public override void _Ready()
    {
        // Crea manager, el tablero, controlador de las interfaces e inicializa el estado del juego en fase de despliegue
        Instance = this;
        SpawnBoard();
        SpawnCamera();

        _deploymentUI = GetNode<DeploymentUI>("DeploymentUI");

        _deployment = new DeploymentController();
        AddChild(_deployment);
        _deployment.Initialize(this, _board, _deploymentUI);

        State = GameState.DEPLOYMENT;
        _deploymentUI.ShowUI();
    }

    // Instancia el tablero
    private void SpawnBoard()
    {
        PackedScene boardScene = GD.Load<PackedScene>(BOARD_SCENE_PATH);
        _board = boardScene.Instantiate<Board>();
        AddChild(_board);
        // Lo inicializa con referencia al game manager
        _board.Initialize(this);
    }

    // Crea una camara en el centro del tablero
    private void SpawnCamera()
    {
        _camera = new Camera2D
        {
            Zoom = new Vector2(0.8f, 0.8f),
            Position = _board.GetBoardCenter(),
            Enabled = true
        };
        AddChild(_camera);
    }

    // Getters
    public Board GetBoard() => _board;
    public DeploymentController GetDeploymentController() => _deployment;
    public bool CanInteract() => State == GameState.WAITING_INPUT;
    public bool IsPlayersTurn(PieceOwner owner) => owner == CurrentTurn;
    public void StartGame()
    {
        State = GameState.WAITING_INPUT;
        CheckGameEnd();
    }

    // Cambia el turno
    public void EndTurn()
    {
        CheckGameEnd();
        if (State == GameState.GAME_OVER) return;

        TurnNumber++;

        CurrentTurn = CurrentTurn == PieceOwner.PLAYER ? PieceOwner.BOT : PieceOwner.PLAYER;
        State = GameState.WAITING_INPUT;

        if (CurrentTurn == PieceOwner.BOT)
        {
            PlayBotTurn();
        }
    }

    private void CheckGameEnd()
    {
        bool playerCoreAlive = _board.HasEnergyCore(PieceOwner.PLAYER);
        bool botCoreAlive = _board.HasEnergyCore(PieceOwner.BOT);
        bool currentHasMoves = _board.HasAnyMoves(CurrentTurn);

        if (!playerCoreAlive)
        {
            GD.Print("GAME OVER: PLAYER perdió su CORE. Gana BOT.");
            State = GameState.GAME_OVER;
            return;
        }

        if (!botCoreAlive)
        {
            GD.Print("GAME OVER: BOT perdió su CORE. Gana PLAYER.");
            State = GameState.GAME_OVER;
            return;
        }

        if (!currentHasMoves)
        {
            PieceOwner winner = CurrentTurn == PieceOwner.PLAYER ? PieceOwner.BOT : PieceOwner.PLAYER;
            GD.Print($"GAME OVER: {CurrentTurn} no tiene movimientos posibles. Gana {winner}.");
            State = GameState.GAME_OVER;
        }

        if (!currentHasMoves)
        {
            PieceOwner opponent = CurrentTurn == PieceOwner.PLAYER ? PieceOwner.BOT : PieceOwner.PLAYER;
            bool opponentHasMoves = _board.HasAnyMoves(opponent);

            if (!opponentHasMoves)
            {
                GD.Print("GAME OVER: Ningún bando tiene movimientos posibles. Empate.");
            }
            else
            {
                GD.Print($"GAME OVER: {CurrentTurn} no tiene movimientos posibles. Gana {opponent}.");
            }

            State = GameState.GAME_OVER;
        }
    }

    private void PlayBotTurn()
    {
        var actions = _board.GetAllPossibleActions(PieceOwner.BOT);

        if (actions.Count == 0)
        {
            EndTurn();
            return;
        }

        var action = actions[_rng.Next(actions.Count)];

        _board.ExecuteBotAction(action);

        EndTurn();
    }

}