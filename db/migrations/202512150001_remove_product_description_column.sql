-- Migration: suppression de la colonne Description des produits
DROP INDEX IF EXISTS "IX_Product_Shop_Descr_trgm";
ALTER TABLE "Product" DROP COLUMN IF EXISTS "Description";
