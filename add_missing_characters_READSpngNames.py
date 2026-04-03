"""
Scans the assets folder for character ID subdirectories not present in the
database JSON and appends them with an empty name for manual editing.

Usage:
    python add_missing_characters.py
    python add_missing_characters.py --db Nikkes/l2d.json --assets Nikkes/l2d
"""

import argparse
import json
import os


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default="Nikkes/l2d.json", help="Path to database JSON file")
    parser.add_argument("--assets", default="Nikkes/l2d", help="Path to assets folder")
    args = parser.parse_args()

    db_path = args.db
    assets_folder = args.assets

    if not os.path.isfile(db_path):
        print(f"ERROR: Database not found: {db_path}")
        return
    if not os.path.isdir(assets_folder):
        print(f"ERROR: Assets folder not found: {assets_folder}")
        return

    with open(db_path, "r", encoding="utf-8") as f:
        database = json.load(f)

    existing_ids = {entry["id"] for entry in database}

    # Collect all immediate subdirectory names as candidate character IDs
    folder_ids = sorted(
        name for name in os.listdir(assets_folder)
        if os.path.isdir(os.path.join(assets_folder, name))
    )

    missing = [fid for fid in folder_ids if fid not in existing_ids]

    if not missing:
        print("No missing characters found. Database is up to date.")
        return

    print(f"Found {len(missing)} missing character(s):")
    for fid in missing:
        print(f"  {fid}")

    for fid in missing:
        database.append({"name": "", "id": fid})

    # Sort by id so the file stays tidy
    database.sort(key=lambda e: e["id"])

    with open(db_path, "w", encoding="utf-8") as f:
        json.dump(database, f, indent=2, ensure_ascii=False)

    print(f"\nAdded {len(missing)} entr{'y' if len(missing) == 1 else 'ies'} to {db_path}.")
    print('Search for `"name": ""` in the file to find entries that need names.')


if __name__ == "__main__":
    main()
