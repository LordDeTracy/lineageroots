# Lineage Roots

A free, cross-platform family history program, with its source code published for anyone to read and improve. Lineage Roots opens **AQ 16** databases
directly. You don't need AQ installed and you don't need to export from it first. It also reads and writes
**GEDCOM 5.5.1**, so it works with every other genealogy program.

> Status: **0.1, early development.** Everything listed below works and is covered by automated tests. The
> FamilySearch features need an app key and have only been tested against simulated responses so far.

## Why

- **Your data, in an open format.** A `.lineage` file is a plain SQLite database. Saves are transactional
  and atomic, so you never need a "check/repair" step.
- **Fast.** The whole tree lives in memory and every view is a lookup. Importing a 13,000-person AQ database takes
  about 0.4 s.
- **Faithful migration.** AQ record numbers (RIN/MRIN) are kept as Lineage Roots ids, and everything AQ stores comes
  across:
  - names, events, dates (qualifiers, dual dates, ranges and periods), places, notes
  - sources, citations, repositories
  - per-parent child relationships, LDS ordinances with temples and statuses
  - scrapbook links, research items, tags, colours, DNA results, FamilySearch IDs
  - deleted records are recognised and skipped.

## Features (0.1)

**Your tree**
- Import AQ 16 (`.aq`) and GEDCOM 5.5 / 5.5.1 (`.ged`), including AQ's custom tags; export GEDCOM 5.5.1
- **Home page**: home person, bookmarks, birthdays and anniversaries coming up, open research, "tree health"
  (people with no sources, dates or parents) and the most common surnames
- **People**: instant search by name, year or ID, with an optional "sounds like" mode (Smith finds Smyth)
- **Person editor**: names and alternate names, life dates, relationship to each parent (birth, adopted, step…),
  tags, colour coding, LDS ordinances including sealing to parents, DNA tests, contact details and reference numbers
- **Event editor** for any event type, with date checking, place, GPS, photos and source citations
- **Sources, citations and repositories**, with scans of the original record on each source
- **Research to-do list** linked to people, with priorities and a done filter
- **Undo and redo** for every edit (Ctrl+Z / Ctrl+Y)

**Charts and maps**
- Pedigree, fan, descendant and hourglass charts. Click to centre, double-click to open, mouse wheel to zoom,
  drag to pan. Save as a picture, or print as a wall chart (one big poster, or pages to tape together)
- A map of where a family lived, with arrows from each parent's birthplace to the child's, and a map of every
  place in the tree (street maps from OpenStreetMap, or a plain grid when that is turned off)

**Places and photos**
- Places page: GPS for a whole place at once, rename or merge, and suggested clean-ups ("Leeds, Yorks" →
  "Leeds, Yorkshire, England")
- Photos on people, events, marriages and sources; a profile picture; tag people in group photos; GPS and date
  read from the camera; copy every photo into a folder beside the tree so they travel together; find moved photos

**Checking your work**
- Find and merge duplicates (merges keep everything and can be undone)
- Problem finder: impossible or unlikely dates and relationships
- Relationship calculator ("2nd cousin once removed", "wife's aunt") with the line of people between them
- Compare with another tree (a cousin's GEDCOM): who matches, what differs, and copy people across

**Reports and sharing**
- PDF or Word: family group sheet, individual summary, ancestors, descendants (outline or narrative "register"
  style), lists of places and surnames, a birthday calendar, and a whole family history book with contents,
  sources and an index
- A family website (plain HTML that works offline from a USB stick or any web host)
- Privacy: GEDCOM and website exports can hide or leave out living people, private events, notes and photos

**Safety**
- Each save keeps the previous version in a backups folder; while there are unsaved changes a recovery copy is
  written every few minutes and offered back after a crash; closing asks before discarding changes
- Several trees open at once, each in its own window
- **FamilySearch**: open a linked person on FamilySearch.org, find and link people, compare and copy details, and
  download ancestors. Read-only; needs your own app key from the
  [FamilySearch developer program](https://www.familysearch.org/developers/)
- Light and dark themes; every control has a screen-reader name

## Building

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet run --project src/Lineage.App
dotnet test
```

To open a file at startup, pass its path: `dotnet run --project src/Lineage.App -- family.aq`

## Layout

| Project | Contents |
|---|---|
| `src/Lineage.Core` | the model (`FamilyTree`, `Person`, `Family`, `Event`, `GenDate`…), SQLite storage, AQ and GEDCOM import, GEDCOM export |
| `src/Lineage.App` | the Avalonia desktop app (MVVM, CommunityToolkit.Mvvm) |
| `tests/Lineage.Core.Tests` | xUnit tests for the format, storage, GEDCOM, undo, merging, duplicates and FamilySearch data |
| `tests/Lineage.App.Tests` | headless UI tests that drive the app's workflows (Avalonia.Headless) |

### Tests and private sample data

Some tests check the AQ importer against AQ's **own** GEDCOM export of the same database. They
compare every person, name, sex, date, place, family and child. Those samples are real family data, so they are
never committed. Put pairs such as `family.aq` and `family_aqexport.ged` in a `samples/real/` folder above the
repository, or point `LINEAGE_SAMPLES` at the folder. Without samples, these tests pass as no-ops and every other
test still runs.

## AQ compatibility

The `.aq` reader was written from documentation of the file format. That documentation came from analysing the
program for interoperability and was checked against AQ's own exports of real databases. No AQ code
is used or included. The importer is read-only and never modifies an `.aq` file.

Supported: AQ 16 (`.aq`, format "3600"). Older AQ formats and PAF files can be imported by exporting them to
GEDCOM first.

## Roadmap

- Record hints and FamilySearch upload (deliberately not started yet)
- Per-change history saved inside the file (undo currently lasts for the session)
- GEDCOM media records referenced by pointer (`0 @M1@ OBJE`)

## License

[PolyForm Noncommercial 1.0.0](LICENSE). You may use, copy, change and share Lineage Roots for any
noncommercial purpose: personal and family history research, hobbies, study, teaching, and use by
charities, schools, libraries, archives and government bodies. Commercial use — selling it, building it
into a paid product or service, or using it to run a business — is not allowed.

Because commercial use is excluded, this is a "source-available" licence rather than an open-source one in
the strict sense of the Open Source Definition.

Lineage Roots uses these libraries, each under its own licence: Avalonia, CommunityToolkit.Mvvm,
Microsoft.Data.Sqlite and DocumentFormat.OpenXml (MIT), MetadataExtractor (Apache 2.0) and QuestPDF
(QuestPDF Community License).
