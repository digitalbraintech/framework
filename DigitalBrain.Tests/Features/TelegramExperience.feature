Feature: Pack config form on install (Slice 2 - config-driven experience)
	As a user installing a pack that declares RequiredConfig
	I want the kernel to render an in-app config form on install
	So that submitting it persists my values (encrypted) without leaving the app

	@distribution @config
	Scenario: Installing a pack with RequiredConfig emits a config form and submit persists values
		Given a generic pack "GenericConfiguredPack" declaring 3 required config fields
		When I publish and install the pack
		Then a config form surface is emitted whose tree contains the fields "telegram_token", "llm_provider", "llm_key"
		When I submit configuration for the pack with token "tok-123", provider "openai", key "sk-secret"
		Then the pack config store returns token "tok-123", provider "openai", key "sk-secret"
