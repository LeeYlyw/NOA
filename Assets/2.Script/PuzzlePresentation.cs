using UnityEngine;

public class PuzzlePresentation : MonoBehaviour
{
    CoopShapePuzzle puzzle;
    readonly Renderer[] pads = new Renderer[2];
    readonly Light[] lamps = new Light[2];
    Renderer gate;
    MaterialPropertyBlock block;
    bool blue, orange, solved;
    int completed, wrongVersion;
    float errorUntil;

    void Start()
    {
        puzzle = GetComponent<CoopShapePuzzle>();
        block = new MaterialPropertyBlock();
        string[] names = { "Blue description pad", "Orange selection pad" };
        for (int i = 0; i < 2; i++)
        {
            Transform pad = transform.Find(names[i]);
            if (pad == null) continue;
            pads[i] = pad.GetComponent<Renderer>();
            GameObject lamp = new GameObject("Pad readiness light");
            lamp.transform.SetParent(pad, false);
            lamp.transform.position = pad.position + Vector3.up * 0.6f;
            lamps[i] = lamp.AddComponent<Light>();
            lamps[i].range = 3; lamps[i].shadows = LightShadows.None;
        }
        Transform door = transform.Find("Sigil locked room gate");
        if (door != null) gate = door.GetComponent<Renderer>();
        GamePresentation.Ensure();
    }

    void Update()
    {
        if (puzzle == null) return;
        GamePresentation feedback = GamePresentation.Ensure();
        bool b = puzzle.BluePadOccupied, o = puzzle.OrangePadOccupied;
        if (b != blue || o != orange)
        {
            if (b && o) feedback.Notify("두 발판 준비 완료 · 도형의 세 가지 특징을 맞추세요", "pad");
            else if ((blue && orange) && !puzzle.IsSolved)
                feedback.Notify("발판에서 벗어났습니다 · 다시 올라가면 이어서 진행합니다", "");
            else if (b || o) feedback.Notify("한 명 준비 완료 · 동료의 발판을 기다립니다", "pad");
        }
        if (puzzle.CompletedRounds > completed && !puzzle.IsSolved)
            feedback.Notify("정답 · " + puzzle.CompletedRounds + " / " + puzzle.TotalRounds + " 단계 완료", "correct");
        if (puzzle.WrongFeedbackVersion != wrongVersion)
        {
            errorUntil = Time.unscaledTime + 0.65f;
            feedback.Notify("오답 · 1라운드 새 문제부터 다시 시작합니다", "wrong");
        }
        if (puzzle.IsSolved && !solved)
            feedback.Notify("잠금 해제 · 열린 방 안의 단서를 확보하세요", "solved");
        blue = b; orange = o; solved = puzzle.IsSolved;
        completed = puzzle.CompletedRounds; wrongVersion = puzzle.WrongFeedbackVersion;
        for (int i = 0; i < 2; i++)
        {
            bool ready = i == 0 ? b : o;
            Color tint = solved ? new Color(0.2f, 0.95f, 0.6f) :
                Time.unscaledTime < errorUntil ? new Color(1, 0.1f, 0.08f) :
                i == 0 ? new Color(0.1f, 0.7f, 1) : new Color(1, 0.45f, 0.08f);
            Paint(pads[i], tint * (ready || solved ? 1 : 0.35f));
            if (lamps[i] != null)
            {
                lamps[i].color = tint;
                lamps[i].intensity = ready || solved ? 1.3f : 0.15f;
            }
        }
        if (solved) Paint(gate, new Color(0.15f, 0.8f, 0.45f));
    }

    void Paint(Renderer target, Color color)
    {
        if (target == null) return;
        target.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        target.SetPropertyBlock(block);
    }
}
