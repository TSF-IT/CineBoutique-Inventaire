-- Migration: relax product EAN length to 64 characters
ALTER TABLE "Product"
    ALTER COLUMN "Ean" TYPE VARCHAR(64);
