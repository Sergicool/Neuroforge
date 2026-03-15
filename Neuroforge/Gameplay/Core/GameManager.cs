using Godot;

public partial class GameManager : Node
{
    // Externo
    private const string BOARD_SCENE_PATH = "res://Gameplay/Board/Board.tscn";

    public static GameManager Instance { get; private set; }    // Instancia que se crea al iniciarse la escena de la partida
    private Board _board;
    private Camera2D _camera;
    private DeploymentUI _deploymentUI;                         // Interfaz del despliegue de piezas
    private DeploymentController _deployment;                   // Logica del despliegue de piezas

    // Controla el estado y el turno actual de la partida
    public PieceOwner CurrentTurn { get; private set; } = PieceOwner.PLAYER;
    public GameState State { get; private set; } = GameState.WAITING_INPUT;

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
    public void StartGame() => State = GameState.WAITING_INPUT;

    // Cambia el turno
    public void EndTurn()
    {
        CurrentTurn = CurrentTurn == PieceOwner.PLAYER ? PieceOwner.BOT : PieceOwner.PLAYER;
        State = GameState.WAITING_INPUT;
    }
}