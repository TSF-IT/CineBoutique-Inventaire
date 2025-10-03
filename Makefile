SHELL := /bin/bash

.PHONY: test test-codex

## test: exécute la suite de tests Domain utilisée par Codex
test: test-codex

## test-codex: lance les tests Domain avec installation locale du SDK si nécessaire
test-codex:
	./scripts/test-codex.sh
