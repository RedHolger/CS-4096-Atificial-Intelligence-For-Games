.PHONY: help build run play console unity docs prepare clean

help:
	@echo "Orchard of the Last Season - Executable Environment"
	@echo ""
	@echo "  make run       Build (if needed) and launch the standalone game"
	@echo "  make build     Build the standalone macOS executable (.app)"
	@echo "  make console   Run the standalone executable attached to terminal"
	@echo "  make prepare   Create/update Main.unity and ProjectSettings via Unity"
	@echo "  make unity     Open the project in Unity 6000.3.25f1 Editor"
	@echo "  make docs      Open Blueprint & Code Walkthrough HTML docs"
	@echo "  make clean     Remove built executables and Unity build logs"

build:
	@./build.sh --target StandaloneOSX

run: play

play:
	@./play.sh

console:
	@./play.sh --console

prepare:
	@./build.sh --prepare-only

unity:
	@./play.sh --unity

docs:
	@./play.sh --docs

clean:
	rm -rf Builds OrchardOfTheLastSeason/Logs/*.log
