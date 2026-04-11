using Godot;
using System;

public partial class GameManager : Node
{
    private const string BOARD_SCENE_PATH = "res://Gameplay/Board/Board.tscn";
    public static GameManager Instance { get; private set; }

    private Board _board;
    private Camera2D _camera;
    private DeploymentUI _deploymentUI;
    private DeploymentController _deployment;
    private RemainingPiecesUI _remainingPiecesUI;
    private CombatUI _combatUI;
    private BotController _bot;

    public PieceOwner CurrentTurn { get; private set; } = PieceOwner.PLAYER;
    public GameState  State       { get; private set; } = GameState.WAITING_INPUT;
    public void SetState(GameState state) => State = state;
    public int        TurnNumber  { get; private set; } = 0;

    public override void _Ready()
    {
        Instance = this;

        SpawnBoard();
        SpawnCamera();

        _deploymentUI = GetNode<DeploymentUI>("CanvasLayer/DeploymentUI");
        _remainingPiecesUI = GetNode<RemainingPiecesUI>("CanvasLayer/RemainingPiecesUI");
        _combatUI = GetNode<CombatUI>("CanvasLayer/CombatUI");

        _deployment = new DeploymentController();
        _deployment.Initialize(this, _board, _deploymentUI);
        _deploymentUI.ShowUI();
        _remainingPiecesUI.HideUI();
        _board.SetCombatUI(_combatUI);

        _bot = new BotController(_board);
        State = GameState.DEPLOYMENT;
    }

    // ==================== Getters ====================

    public Board GetBoard()                                 => _board;
    public DeploymentController GetDeploymentController()   => _deployment;
    public bool CanInteract()                               => State == GameState.WAITING_INPUT;
    public bool IsPlayersTurn(PieceOwner owner)             => owner == CurrentTurn;

    // ==================== Flujo de partida ====================

    public void StartGame()
    {
        _remainingPiecesUI.ShowUI();
        RefreshRemainingPiecesUI();
        State = GameState.WAITING_INPUT;
        CheckGameEnd();
    }

    // TODO Comprobar o evitar que si el jugador pierde pueda hacer un movimiento mas antes de acabar la partida
    public void EndTurn()
    {
        RefreshRemainingPiecesUI();
        CheckGameEnd();
        if (State == GameState.GAME_OVER) return;

        TurnNumber++;
        CurrentTurn = CurrentTurn == PieceOwner.PLAYER ? PieceOwner.BOT : PieceOwner.PLAYER;
        State       = GameState.WAITING_INPUT;

        if (CurrentTurn == PieceOwner.BOT)
            _bot.PlayTurn(this);

    }

    // Comprueba las condiciones de fin de partida
    private void CheckGameEnd()
    {
        if (!_board.HasEnergyCore(PieceOwner.PLAYER))
        {
            GD.Print("GAME OVER: PLAYER perdió su ENERGY CORE. Gana BOT.");
            State = GameState.GAME_OVER;
            return;
        }

        if (!_board.HasEnergyCore(PieceOwner.BOT))
        {
            GD.Print("GAME OVER: BOT perdió su ENERGY CORE. Gana PLAYER.");
            State = GameState.GAME_OVER;
            return;
        }

        bool currentHasMoves  = _board.HasAnyMoves(CurrentTurn);
        if (!currentHasMoves)
        {
            PieceOwner opponent        = CurrentTurn == PieceOwner.PLAYER ? PieceOwner.BOT : PieceOwner.PLAYER;
            bool       opponentHasMoves = _board.HasAnyMoves(opponent);

            string msg = opponentHasMoves
                ? $"GAME OVER: {CurrentTurn} no tiene movimientos. Gana {opponent}."
                : "GAME OVER: Ningún bando tiene movimientos. Empate.";

            GD.Print(msg);
            State = GameState.GAME_OVER;
        }
    }

    public void RefreshRemainingPiecesUI()
    {
        _remainingPiecesUI.UpdateCounts(PieceOwner.PLAYER, _board.GetRemainingCounts(PieceOwner.PLAYER));
        _remainingPiecesUI.UpdateCounts(PieceOwner.BOT, _board.GetRemainingCounts(PieceOwner.BOT));
    }

    // ==================== Inicialización de escena ====================

    private void SpawnBoard()
    {
        PackedScene boardScene = GD.Load<PackedScene>(BOARD_SCENE_PATH);
        _board = boardScene.Instantiate<Board>();
        AddChild(_board);
        _board.Initialize(this);
    }

    private void SpawnCamera()
    {
        _camera = new Camera2D
        {
            Zoom     = new Godot.Vector2(0.8f, 0.8f),
            Position = _board.GetBoardCenter() + new Vector2(-60f, 0),
            Enabled  = true
        };
        AddChild(_camera);
    }
}