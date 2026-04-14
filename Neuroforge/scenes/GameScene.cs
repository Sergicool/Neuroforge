using Godot;
using System;
using System.Threading.Tasks;

public partial class GameScene : Node
{
    private const string BOARD_SCENE_PATH = "res://entities/board/Board.tscn";

    private readonly Texture2D _playerIcon = GD.Load<Texture2D>("res://assets/icons/PlayerIcon.png");
    private readonly Texture2D _botIcon = GD.Load<Texture2D>("res://assets/icons/BotIcon.png");
    private readonly Color _playerColor = new Color(0.537f, 0.922f, 1f);
    private readonly Color _botColor = new Color(1f, 0.839f, 0.49f);
    private readonly Color _colorHidden = new Color(1f, 1f, 1f, 0f);

    public static GameScene Instance { get; private set; }

    private Board _board;
    private Camera2D _camera;
    private DeploymentUI _deploymentUI;
    private DeploymentController _deployment;
    private RemainingPiecesUI _remainingPiecesUI;
    private CombatUI _combatUI;
    private Button _pauseButton;
    private PauseMenu _pauseMenu;
    private bool _isPaused = false;
    private EndGameUI _endGameUI;

    private BotController _bot;
    private TextureRect _turnIcon;
    private Panel _turnIndicarotUI;

    public PieceOwner CurrentTurn { get; private set; } = PieceOwner.PLAYER;
    public GameState State { get; private set; } = GameState.WAITING_INPUT;
    public void SetState(GameState state) => State = state;
    public int TurnNumber { get; private set; } = 0;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        SpawnBoard();

        _camera = GetNode<Camera2D>("Camera2D");
        _camera.Position = _board.GetBoardCenter();

        _deploymentUI = GetNode<DeploymentUI>("CanvasLayer/DeploymentUI");
        _remainingPiecesUI = GetNode<RemainingPiecesUI>("CanvasLayer/RemainingPiecesUI");
        _combatUI = GetNode<CombatUI>("CanvasLayer/CombatUI");
        _turnIcon = GetNode<TextureRect>("CanvasLayer/PanelContainer/TextureRect");
        _turnIndicarotUI = GetNode<Panel>("CanvasLayer/PanelContainer");
        _pauseButton = GetNode<Button>("CanvasLayer/PauseButton");
        _pauseButton.Pressed += PauseGame;
        _pauseMenu = GetNode<PauseMenu>("CanvasLayer/PauseMenu");
        _pauseMenu.Visible = false;
        _pauseMenu.Init(this);
        _endGameUI = GetNode<EndGameUI>("CanvasLayer/EndGameUI"); 

        _deployment = new DeploymentController();
        _deployment.Initialize(this, _board, _deploymentUI);
        _deploymentUI.ShowUI();
        _remainingPiecesUI.HideUI();
        _turnIndicarotUI.Visible = false;
        _board.SetCombatUI(_combatUI);

        _bot = new BotController(_board);
        State = GameState.DEPLOYMENT;
    }

    // ==================== Getters ====================

    public Board GetBoard() => _board;
    public DeploymentController GetDeploymentController() => _deployment;
    public bool CanInteract() => State == GameState.WAITING_INPUT;
    public bool IsPlayersTurn(PieceOwner owner) => owner == CurrentTurn;

    // ==================== Flujo de partida ====================

    public void StartGame()
    {
        _remainingPiecesUI.ShowUI();
        _turnIndicarotUI.Visible = true;
        RefreshRemainingPiecesUI();
        SetTurnIconImmediate(PieceOwner.PLAYER);
        State = GameState.WAITING_INPUT;
        CheckGameEnd();
    }

    public async void EndTurn()
    {
        RefreshRemainingPiecesUI();
        CheckGameEnd();
        if (State == GameState.GAME_OVER) return;

        TurnNumber++;
        CurrentTurn = CurrentTurn == PieceOwner.PLAYER ? PieceOwner.BOT : PieceOwner.PLAYER;

        await BlinkTurnIcon(CurrentTurn);

        State = GameState.WAITING_INPUT;

        if (CurrentTurn == PieceOwner.BOT)
            _bot.PlayTurn(this);
    }

    // ==================== Indicador de turno ====================

    private void SetTurnIconImmediate(PieceOwner turn)
    {
        _turnIcon.Texture = turn == PieceOwner.PLAYER ? _playerIcon : _botIcon;
        _turnIcon.Modulate = turn == PieceOwner.PLAYER ? _playerColor : _botColor;
    }

    private async Task BlinkTurnIcon(PieceOwner newTurn)
    {
        const float HALF = 0.05f;

        Texture2D newTexture = newTurn == PieceOwner.PLAYER ? _playerIcon : _botIcon;
        Color newColor = newTurn == PieceOwner.PLAYER ? _playerColor : _botColor;

        Tween tOut = CreateTween();
        tOut.TweenProperty(_turnIcon, "modulate:a", 0f, HALF);
        await ToSignal(tOut, Tween.SignalName.Finished);

        _turnIcon.Texture = newTexture;
        _turnIcon.Modulate = new Color(newColor.R, newColor.G, newColor.B, 0f);

        Tween tIn = CreateTween();
        tIn.TweenProperty(_turnIcon, "modulate:a", 1f, HALF * 2f);
        await ToSignal(tIn, Tween.SignalName.Finished);
    }

    // ==================== Fin de partida y UI ====================

    private void CheckGameEnd()
    {
        string endMessage = "";
        bool isGameOver = false;
        int gameResult = 0; // 0: Lose, 1: Win, 2: Draw

        // Chequeo de Energy cores
        if (!_board.HasEnergyCore(PieceOwner.PLAYER))
        {
            endMessage = "DEFEAT!\nYour energy core has been destroyed";
            gameResult = 0;
            isGameOver = true;
        }
        else if (!_board.HasEnergyCore(PieceOwner.BOT))
        {
            endMessage = "VICTORY!\nYou have destroyed the enemy's energy core";
            gameResult = 1;
            isGameOver = true;
        }

        // Chequeo de Movimientos Bloqueados
        if (!isGameOver)
        {
            bool currentHasMoves = _board.HasAnyMoves(CurrentTurn);
            if (!currentHasMoves)
            {
                PieceOwner opponent = (CurrentTurn == PieceOwner.PLAYER) ? PieceOwner.BOT : PieceOwner.PLAYER;
                bool opponentHasMoves = _board.HasAnyMoves(opponent);

                if (opponentHasMoves)
                {
                    if (CurrentTurn == PieceOwner.BOT)
                    {
                        endMessage = "VICTORY!\nThe opponent has run out of moves.";
                        gameResult = 1;
                    }
                    else
                    {
                        endMessage = "DEFEAT!\nYou've run out of moves";
                        gameResult = 0;
                    }
                }
                else
                {
                    endMessage = "DRAW!\nNo one has any pieces left to move";
                    gameResult = 2;
                }
                isGameOver = true;
            }
        }

        if (isGameOver)
        {
            TriggerEndGame(endMessage, gameResult);
        }
    }

    private void TriggerEndGame(string message, int result)
    {
        State = GameState.GAME_OVER;
        GetTree().Paused = true;
        _endGameUI.Show(message, result);
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

    // ==================== Input ====================

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            TryTogglePause();
        }
    }

    private void TryTogglePause()
    {
        if (_isPaused)
        {
            ResumeGame();
            return;
        }

        if (!CanPause())
            return;

        PauseGame();
    }

    private bool CanPause()
    {
        return State == GameState.WAITING_INPUT || State == GameState.DEPLOYMENT;
    }

    private void PauseGame()
    {
        _isPaused = true;
        GetTree().Paused = true;
        _ = _pauseMenu.ShowMenu();
    }

    public void ResumeGame()
    {
        _isPaused = false;
        GetTree().Paused = false;
        _ = _pauseMenu.HideMenu();
    }

}