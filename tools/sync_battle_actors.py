"""Refresh Unity actor descriptors when the reviewed directional atlases change."""
from hashlib import sha256
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "art"
RESOURCES = ROOT / "unity/Assets/Battle/Resources/Battle/Actors"
ROSTER = ART / "character-sprites/animation-roster.json"

POSE = {
    "idle": "Idle",
    "walk": "Walk",
    "sprint": "Sprint",
    "bloom": "Bloom",
    "shield-break": "ShieldBreak",
    "shield-regrow": "ShieldRegrow",
    "awaken": "Awaken",
}
ACTION = {"attack", "action", "lash", "ram", "spit", "bite", "claw"}


def main():
    roster = json.loads(ROSTER.read_text(encoding="utf-8"))
    RESOURCES.mkdir(parents=True, exist_ok=True)
    for actor in roster["characters"]:
        clips = []
        for clip in actor["clips"]:
            path = (ROOT / clip["file"]).resolve()
            relative = path.relative_to(ART).as_posix()
            cues = clip.get("events", [])
            pose = "Action" if clip["id"] in ACTION else POSE[clip["id"]]
            clips.append({
                "path": relative,
                "pose": pose,
                "frames": clip["frames"],
                "fps": clip["fps"],
                "loop": clip["loop"],
                "cue": cues[0]["frame"] if cues else 0,
                "sourceHash": sha256(path.read_bytes()).hexdigest(),
            })
        assert clips[0]["pose"] == "Idle", actor["id"]
        descriptor = {
            "id": actor["id"],
            "size": actor["frameSize"],
            "columns": roster["columns"],
            "rows": roster["rows"],
            "pixelsPerUnit": roster["pixelsPerUnit"],
            "pivotX": roster["pivot"]["x"],
            "pivotY": roster["pivot"]["y"],
            "clips": clips,
        }
        path = RESOURCES / (actor["id"] + ".battleactor")
        text = json.dumps(descriptor, indent=2) + "\n"
        if not path.exists() or path.read_text() != text:
            path.write_text(text)


if __name__ == "__main__":
    main()
