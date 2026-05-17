-- ============================================================
--  DuneFlame - Migration: AddShippingTranslations
--  Safe to run multiple times (idempotent)
-- ============================================================

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260511121810_AddShippingTranslations') THEN
    CREATE TABLE "CityTranslations" (
        "Id"             uuid                     NOT NULL,
        "CityId"         uuid                     NOT NULL,
        "LanguageCode"   character varying(5)     NOT NULL,
        "TranslatedName" character varying(150)   NOT NULL,
        "CreatedAt"      timestamp with time zone NOT NULL,
        "UpdatedAt"      timestamp with time zone,
        CONSTRAINT "PK_CityTranslations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CityTranslations_Cities_CityId"
            FOREIGN KEY ("CityId") REFERENCES "Cities" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260511121810_AddShippingTranslations') THEN
    CREATE TABLE "CountryTranslations" (
        "Id"             uuid                     NOT NULL,
        "CountryId"      uuid                     NOT NULL,
        "LanguageCode"   character varying(5)     NOT NULL,
        "TranslatedName" character varying(150)   NOT NULL,
        "CreatedAt"      timestamp with time zone NOT NULL,
        "UpdatedAt"      timestamp with time zone,
        CONSTRAINT "PK_CountryTranslations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CountryTranslations_Countries_CountryId"
            FOREIGN KEY ("CountryId") REFERENCES "Countries" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260511121810_AddShippingTranslations') THEN
    CREATE UNIQUE INDEX "IX_CityTranslations_CityId_LanguageCode"
        ON "CityTranslations" ("CityId", "LanguageCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260511121810_AddShippingTranslations') THEN
    CREATE UNIQUE INDEX "IX_CountryTranslations_CountryId_LanguageCode"
        ON "CountryTranslations" ("CountryId", "LanguageCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260511121810_AddShippingTranslations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260511121810_AddShippingTranslations', '10.0.1');
    END IF;
END $EF$;

COMMIT;
